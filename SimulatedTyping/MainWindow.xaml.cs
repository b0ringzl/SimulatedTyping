using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace SimulatedTyping;

public partial class MainWindow : Window
{
    private const int StartHotkeyId = 1;
    private const int StopHotkeyId = 2;
    private const int ReadyHotkeyId = 3;
    private const int WmHotkey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private const uint F8 = 0x77;
    private const uint F9 = 0x78;
    private const uint F7 = 0x76;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private readonly Forms.NotifyIcon _tray;
    private CancellationTokenSource? _runCancel;
    private TypingEngine? _engine;
    private TypingOptions? _armedOptions;
    private IReadOnlyList<TypingUnit>? _armedUnits;
    private bool _capturingSelection;
    private int _armVersion;
    private ReadyToastWindow? _readyToast;
    private ProgressOverlayWindow? _progressOverlay;
    private IntPtr _windowHandle;
    private bool _hotkeysReady;

    public MainWindow()
    {
        InitializeComponent();
        _tray = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "模拟手打输入",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("显示窗口", null, (_, _) => Dispatcher.Invoke(ShowWindow));
        _tray.ContextMenuStrip.Items.Add("停止输入", null, (_, _) => Dispatcher.Invoke(Stop));
        _tray.ContextMenuStrip.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWindow);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_windowHandle)?.AddHook(HotkeyHook);
        var ready = RegisterHotKey(_windowHandle, ReadyHotkeyId, ModControl | ModNoRepeat, F7);
        var start = RegisterHotKey(_windowHandle, StartHotkeyId, ModControl | ModNoRepeat, F8);
        var stop = RegisterHotKey(_windowHandle, StopHotkeyId, ModControl | ModNoRepeat, F9);
        _hotkeysReady = ready && start && stop;
        if (!_hotkeysReady)
        {
            if (ready) UnregisterHotKey(_windowHandle, ReadyHotkeyId);
            if (start) UnregisterHotKey(_windowHandle, StartHotkeyId);
            if (stop) UnregisterHotKey(_windowHandle, StopHotkeyId);
            StatusText.Text = "快捷键注册失败；请关闭占用 Ctrl+F7/F8/F9 的程序后重启";
        }
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey) return IntPtr.Zero;
        handled = true;
        if (wParam.ToInt32() == ReadyHotkeyId) _ = ArmFromHotkeyAsync();
        else if (wParam.ToInt32() == StartHotkeyId) _ = StartOrToggleAsync();
        else if (wParam.ToInt32() == StopHotkeyId) Stop();
        return IntPtr.Zero;
    }

    private async Task StartOrToggleAsync()
    {
        if (_engine is not null)
        {
            _engine.TogglePause();
            return;
        }
        if (_armedOptions is null || _armedUnits is null)
        {
            SetStatus("请先选中文字并按 Ctrl+F7 就绪，或在本窗口粘贴内容后点“就绪并隐藏”");
            return;
        }
        var target = KeyboardInput.GetForegroundWindow();
        if (target == IntPtr.Zero || target == _windowHandle)
        {
            SetStatus("请先点选目标输入框，再按 Ctrl+F8");
            return;
        }
        await RunAsync(_armedUnits, _armedOptions, target);
    }

    private async Task<bool> RunAsync(IReadOnlyList<TypingUnit> units, TypingOptions options, IntPtr target)
    {
        if (_engine is not null) return false;
        _runCancel = new CancellationTokenSource();
        var overlay = new ProgressOverlayWindow(target);
        _progressOverlay = overlay;
        _engine = new TypingEngine(units, options, target,
            (status, done, total) =>
            {
                void Update()
                {
                    StatusText.Text = status;
                    ProgressText.Text = $"{done} / {total} 字";
                    _tray.Text = status.Length > 63 ? status[..63] : status;
                    overlay.Update(status, done, total);
                }
                if (Dispatcher.CheckAccess()) Update();
                else Dispatcher.BeginInvoke(Update);
            });
        var completed = false;
        try
        {
            overlay.ShowForRun();
            // A global hotkey fires before Ctrl is necessarily released.
            for (var i = 0; i < 100 && !KeyboardInput.ModifiersAreReleased; i++)
                await Task.Delay(30, _runCancel.Token);
            if (!KeyboardInput.ModifiersAreReleased)
                throw new InvalidOperationException("请松开 Ctrl/Shift/Alt 后重新启动输入。");
            // Some IMEs continue processing the shortcut briefly after key-up.
            await Task.Delay(800, _runCancel.Token);
            await _engine.RunAsync(_runCancel.Token);
            completed = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            SetStatus("已停止");
        }
        catch (Exception ex)
        {
            SetStatus("输入失败：" + ex.Message);
            ShowWindow();
        }
        finally
        {
            await overlay.FadeOutAsync(completed);
            if (ReferenceEquals(_progressOverlay, overlay)) _progressOverlay = null;
            _engine = null;
            _runCancel?.Dispose();
            _runCancel = null;
            _armedOptions = null;
            _armedUnits = null;
        }
        return false;
    }

    private void ArmButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_hotkeysReady)
        {
            System.Windows.MessageBox.Show("全局快捷键注册失败，请关闭占用快捷键的程序后重启。", "无法就绪");
            return;
        }
        if (!TryPrepare(out var options, out var units)) return;
        _armedOptions = options;
        _armedUnits = units;
        SetStatus("已就绪；点选目标输入框后按 Ctrl+F8");
        Hide();
    }

    private async Task ArmFromHotkeyAsync()
    {
        if (_engine is not null || _capturingSelection) return;
        _capturingSelection = true;
        var version = ++_armVersion;
        _armedOptions = null;
        _armedUnits = null;
        var sourceWindow = KeyboardInput.GetForegroundWindow();
        try
        {
            if (sourceWindow == IntPtr.Zero) throw new InvalidOperationException("没有可读取的前台窗口。");
            for (var i = 0; i < 100 && !KeyboardInput.ModifiersAreReleased; i++)
                await Task.Delay(30);
            if (!KeyboardInput.ModifiersAreReleased)
                throw new InvalidOperationException("请松开 Ctrl/Shift/Alt 后重新选择文字。");
            await Task.Delay(120);
            if (version != _armVersion) return;
            var selected = await SelectedTextCapture.ReadAsync(sourceWindow);
            if (version != _armVersion) return;
            if (string.IsNullOrWhiteSpace(selected))
            {
                SetStatus("未检测到选中的文字；请选中文字后按 Ctrl+F7");
                ShowToast(sourceWindow, "未检测到选中文字", "请先选中文字，再按 Ctrl+F7");
                return;
            }
            SourceText.Text = selected;
            if (!TryPrepare(out var options, out var units))
            {
                ShowWindow();
                return;
            }
            _armedOptions = options;
            _armedUnits = units;
            SetStatus($"已读取 {selected.Length} 字；点选输入位置后按 Ctrl+F8");
            Hide();
            ShowToast(sourceWindow, "程序已就绪", $"已读取 {selected.Length} 字  ·  Ctrl+F8 开始");
        }
        catch (Exception ex)
        {
            SetStatus("读取选中文字失败：" + ex.Message);
            if (sourceWindow != IntPtr.Zero)
                ShowToast(sourceWindow, "读取选中文字失败", "请检查选区并重试 Ctrl+F7");
        }
        finally { _capturingSelection = false; }
    }

    private void ShowToast(IntPtr screenWindow, string title, string detail)
    {
        _readyToast?.Close();
        var toast = new ReadyToastWindow(screenWindow, title, detail);
        _readyToast = toast;
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_readyToast, toast)) _readyToast = null;
        };
        toast.ShowForTwoSeconds();
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine is not null) return;
        if (!TryPrepare(out var options, out var units)) return;
        var expected = NormalizeNewlines(SourceText.Text);
        PreviewText.Clear();
        PreviewText.Focus();
        PreviewText.CaretIndex = 0;
        if (await RunAsync(units, options, _windowHandle))
        {
            // SendInput can return before the target TextBox handles its final key events.
            for (var i = 0; i < 10 && NormalizeNewlines(PreviewText.Text) != expected; i++)
                await Task.Delay(100);
            SetStatus(NormalizeNewlines(PreviewText.Text) == expected
                ? "预览与原文一致"
                : "预览与原文不一致；请检查目标输入法是否为中文拼音");
        }
    }

    private bool TryPrepare(out TypingOptions options, out IReadOnlyList<TypingUnit> units)
    {
        options = null!;
        units = Array.Empty<TypingUnit>();
        if (string.IsNullOrWhiteSpace(SourceText.Text))
        {
            SetStatus("请先粘贴要输入的内容");
            return false;
        }
        var range = RangeMode.IsChecked == true;
        var valid = range
            ? TrySpeed(MinSpeed.Text, out _) && TrySpeed(MaxSpeed.Text, out _) &&
              int.Parse(MinSpeed.Text) <= int.Parse(MaxSpeed.Text)
            : TrySpeed(FixedSpeed.Text, out _);
        if (!valid)
        {
            SetStatus("速度请填写 1～500 字/分钟，且最低速度不能超过最高速度");
            return false;
        }
        if (!int.TryParse(SaveWaitSeconds.Text, out var saveWaitSeconds) || saveWaitSeconds is < 1 or > 120)
        {
            SetStatus("参考等待请填写 1～120 秒；触发时会额外加 1 秒");
            return false;
        }
        var fixedSpeed = range ? 60 : int.Parse(FixedSpeed.Text);
        var minSpeed = range ? int.Parse(MinSpeed.Text) : fixedSpeed;
        var maxSpeed = range ? int.Parse(MaxSpeed.Text) : fixedSpeed;
        options = new TypingOptions(range, fixedSpeed, minSpeed, maxSpeed,
            (int)ErrorRate.Value, InputMode.SelectedIndex == 0, NewlineMode.SelectedIndex,
            (int)SentencePauseRate.Value, (int)ParagraphPauseRate.Value, saveWaitSeconds);
        try { units = TypingPlan.Build(SourceText.Text); }
        catch (Exception ex)
        {
            SetStatus("无法生成拼音计划：" + ex.Message);
            return false;
        }
        if (units.Count == 0) return false;
        PlanText.Text = "输入计划：" + string.Join(" / ", units.Where(u => u.Kind == UnitKind.Hanzi).Take(14).Select(u => u.Text)) +
                        (units.Count > 14 ? " …" : "") +
                        $"；停顿：句末 {options.SentencePauseRate}% / 段末 {options.ParagraphPauseRate}%，每次 {options.SaveWaitSeconds + 1} 秒";
        ProgressText.Text = $"0 / {units.Sum(u => u.Text.Length)} 字";
        return true;
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool TrySpeed(string value, out int speed) =>
        int.TryParse(value, out speed) && speed is >= 1 and <= 500;

    private void StopButton_Click(object sender, RoutedEventArgs e) => Stop();

    private void Stop()
    {
        _runCancel?.Cancel();
        _armedOptions = null;
        _armedUnits = null;
        _armVersion++;
        if (_engine is null) SetStatus("已停止");
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
        _tray.Text = status.Length > 63 ? status[..63] : status;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void SourceText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CountText is not null && SourceText is not null)
            CountText.Text = $"{SourceText.Text.Length} 字";
        if (PlanText is not null) PlanText.Text = "";
    }

    private void SpeedMode_Changed(object sender, RoutedEventArgs e)
    {
        if (FixedSpeed is null || MinSpeed is null || MaxSpeed is null) return;
        var range = RangeMode.IsChecked == true;
        FixedSpeed.IsEnabled = !range;
        MinSpeed.IsEnabled = range;
        MaxSpeed.IsEnabled = range;
    }

    private void ErrorRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ErrorRateText is not null) ErrorRateText.Text = $"{(int)e.NewValue}%";
    }

    private void SavePauseRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SentencePauseRateText is not null && SentencePauseRate is not null)
            SentencePauseRateText.Text = $"{(int)SentencePauseRate.Value}%";
        if (ParagraphPauseRateText is not null && ParagraphPauseRate is not null)
            ParagraphPauseRateText.Text = $"{(int)ParagraphPauseRate.Value}%";
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized) Hide();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _runCancel?.Cancel();
        _armVersion++;
        _readyToast?.Close();
        _progressOverlay?.Close();
        if (_hotkeysReady)
        {
            UnregisterHotKey(_windowHandle, ReadyHotkeyId);
            UnregisterHotKey(_windowHandle, StartHotkeyId);
            UnregisterHotKey(_windowHandle, StopHotkeyId);
        }
        _tray.Visible = false;
        _tray.Dispose();
    }
}
