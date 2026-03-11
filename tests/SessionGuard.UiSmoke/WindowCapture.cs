using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SessionGuard.UiSmoke;

internal static class WindowCapture
{
    public static void SaveToFile(IntPtr windowHandle, string outputPath)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Cannot capture a window with an empty handle.");
        }

        if (!GetWindowRect(windowHandle, out var rect))
        {
            throw new InvalidOperationException("Failed to read the target window bounds.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Target window bounds were empty.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();
        var printWindowSucceeded = false;

        try
        {
            printWindowSucceeded = PrintWindow(windowHandle, hdc, 0);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        if (!printWindowSucceeded)
        {
            graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
        }

        bitmap.Save(outputPath, ImageFormat.Png);
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
