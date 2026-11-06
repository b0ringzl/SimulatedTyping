using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace SimulatedTyping;

internal sealed class ProgressOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private readonly IntPtr _target;
    private readonly Path _arc;
    private readonly Ellipse _fullRing;
    private readonly TextBlock _percent;
    private readonly TextBlock _state;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y,
                                             int width, int height, uint flags);

    public ProgressOverlayWindow(IntPtr target)
    {
        _target = target;
        Width = 132;
        Height = 132;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        AllowsTransparency = true;
        Background = Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Opacity = 0;

        var circle = new Grid { Width = 116, Height = 116 };
        circle.Children.Add(new Border
        {
            Background = new SolidColorBrush(Media.Color.FromArgb(238, 31, 47, 76)),
            CornerRadius = new CornerRadius(58),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.3 }
        });
        circle.Children.Add(new Ellipse
        {
            Width = 94, Height = 94, StrokeThickness = 8,
            Stroke = new SolidColorBrush(Media.Color.FromRgb(80, 99, 128))
        });
        _fullRing = new Ellipse
        {
            Width = 94, Height = 94, StrokeThickness = 8,
            Stroke = new SolidColorBrush(Media.Color.FromRgb(100, 213, 255)),
            Visibility = Visibility.Collapsed
        };
        circle.Children.Add(_fullRing);
        _arc = new Path
        {
            Stroke = new SolidColorBrush(Media.Color.FromRgb(100, 213, 255)),
            StrokeThickness = 8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Visibility = Visibility.Collapsed
        };
        circle.Children.Add(_arc);
        _percent = new TextBlock
        {
            Text = "0%", Foreground = Media.Brushes.White,
            FontFamily = new Media.FontFamily("Microsoft YaHei UI"),
            FontSize = 23, FontWeight = FontWeights.Bold,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -12, 0, 0)
        };
        circle.Children.Add(_percent);
        _state = new TextBlock
        {
            Text = "准备中", Foreground = new SolidColorBrush(Media.Color.FromRgb(205, 221, 244)),
            FontFamily = new Media.FontFamily("Microsoft YaHei UI"),
            FontSize = 11,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 38, 0, 0)
        };
        circle.Children.Add(_state);
        Content = circle;
        SourceInitialized += (_, _) => PositionWithoutActivation();
    }

    public void ShowForRun()
    {
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
    }

    public void Update(string status, int done, int total)
    {
        var ratio = total <= 0 ? 0 : Math.Clamp(done / (double)total, 0, 1);
        _percent.Text = $"{(ratio >= 1 ? 100 : (int)Math.Floor(ratio * 100))}%";
        _fullRing.Visibility = ratio >= 1 ? Visibility.Visible : Visibility.Collapsed;
        _arc.Visibility = ratio > 0 && ratio < 1 ? Visibility.Visible : Visibility.Collapsed;
        if (ratio > 0 && ratio < 1)
        {
            const double center = 58;
            const double radius = 47;
            var angle = ratio * 2 * Math.PI;
            var end = new System.Windows.Point(center + radius * Math.Sin(angle),
                                               center - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = new System.Windows.Point(center, center - radius) };
            figure.Segments.Add(new ArcSegment(end, new System.Windows.Size(radius, radius), 0,
                                               ratio > 0.5, SweepDirection.Clockwise, true));
            _arc.Data = new PathGeometry { Figures = { figure } };
        }
        _state.Text = status.Contains("暂停") || status.Contains("已切换") ? "已暂停" :
                      status.Contains("停顿") ? "停顿中" :
                      status.StartsWith("已完成") ? "已完成" : "输入中";
    }

    public async Task FadeOutAsync(bool completed)
    {
        if (!IsVisible) return;
        if (completed) await Task.Delay(450);
        if (!IsVisible) return;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)));
        await Task.Delay(300);
        if (IsVisible) Close();
    }

    private void PositionWithoutActivation()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle,
            new IntPtr(style | WsExTransparent | WsExToolWindow | WsExNoActivate));
        if (!GetWindowRect(hwnd, out var rect)) return;
        var area = Forms.Screen.FromHandle(_target).WorkingArea;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = area.Right - width - 24;
        var y = area.Bottom - height - 24;
        SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }
}
