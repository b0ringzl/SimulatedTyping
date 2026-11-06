using System.Runtime.InteropServices;
using System.Windows;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace SimulatedTyping;

internal static class SelectedTextCapture
{
    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    public static async Task<string?> ReadAsync(IntPtr sourceWindow)
    {
        // Keep the complete clipboard data object, including non-text formats.
        var previous = WpfClipboard.GetDataObject();
        var before = GetClipboardSequenceNumber();
        uint copied = before;
        try
        {
            if (KeyboardInput.GetForegroundWindow() != sourceWindow)
                throw new InvalidOperationException("选中文字所在窗口已切换，请重新选择后按 Ctrl+F7。");
            KeyboardInput.ControlC();
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(75);
                copied = GetClipboardSequenceNumber();
                if (copied != before) break;
            }
            if (copied == before) return null;
            return WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText)
                ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
                : WpfClipboard.ContainsText() ? WpfClipboard.GetText() : null;
        }
        finally
        {
            // Restore only our own copy; leave a newer clipboard change alone.
            if (copied != before && GetClipboardSequenceNumber() == copied)
            {
                if (previous is null) WpfClipboard.Clear();
                else WpfClipboard.SetDataObject(previous, true);
            }
        }
    }
}
