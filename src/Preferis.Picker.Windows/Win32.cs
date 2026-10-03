using System.Runtime.InteropServices;

namespace Preferis.Picker.Windows;

/// <summary>Window management WinUI does not offer: foreground, rounded corners, dragging, the monitor's scale.</summary>
internal static partial class Win32
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const uint WmNclButtonDown = 0xA1;
    private const int HtCaption = 2;
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    public static Point CursorPosition() => GetCursorPos(out var point) ? point : default;

    /// <summary>The display scale (1.0 = 96 DPI) of the monitor at a point, in physical pixels.</summary>
    public static double ScaleAt(Point point)
    {
        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
    }

    /// <summary>A window without a title bar is square on Windows 11 unless asked otherwise.</summary>
    public static void RoundCorners(IntPtr window)
    {
        var preference = DwmwcpRound;
        DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    /// <summary>Starts moving the window with the mouse, as if its title bar had been grabbed.</summary>
    public static void BeginDrag(IntPtr window)
    {
        ReleaseCapture();
        SendMessage(window, WmNclButtonDown, HtCaption, IntPtr.Zero);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(Point point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr window, uint message, nint wParam, IntPtr lParam);
}
