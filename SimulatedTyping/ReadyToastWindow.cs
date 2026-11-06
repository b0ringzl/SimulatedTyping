using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace SimulatedTyping;

internal sealed class ReadyToastWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private readonly IntPtr _target;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y,
                                             int width, int height, uint flags);

    public ReadyToastWindow(IntPtr target, string title, string detail)
    {
        _target = target;
        Width = 390;
        Height = 76;
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

        Content = new Border
        {
            Background = new SolidColorBrush(Media.Color.FromRgb(31, 47, 76)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20, 12, 20, 12),
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.25 },
            Child = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = title,
                        Foreground = Media.Brushes.White,
                        FontFamily = new Media.FontFamily("Microsoft YaHei UI"),
                        FontSize = 17,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = detail,
                        Foreground = new SolidColorBrush(Media.Color.FromRgb(204, 219, 245)),
                        FontFamily = new Media.FontFamily("Microsoft YaHei UI"),
                        FontSize = 12,
                        Margin = new Thickness(0, 4, 0, 0),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                    }
                }
            }
        };
        SourceInitialized += (_, _) => PositionWithoutActivation();
    }

    public void ShowForTwoSeconds()
    {
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        _ = DismissAsync();
    }

    private async Task DismissAsync()
    {
        await Task.Delay(1750);
        if (!IsVisible) return;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250)));
        await Task.Delay(250);
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
        var x = area.Left + (area.Width - width) / 2;
        var y = area.Bottom - height - 48;
        SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0,
                     SwpNoSize | SwpNoActivate);
    }
}
