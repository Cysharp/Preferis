using System.Runtime.InteropServices;

namespace Preferis.Settings.Windows;

internal static partial class Win32
{
    private const int SwRestore = 9;
    private const string WinUIWindowClass = "WinUIDesktopWin32WindowClass";

    /// <summary>Brings the running settings window, titled one of <paramref name="titles"/>, to the front (restored when minimized).</summary>
    public static void ActivateWindow(IEnumerable<string> titles)
    {
        var window = titles.Select(title => FindWindow(WinUIWindowClass, title)).FirstOrDefault(handle => handle != IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            return;
        }
        if (IsIconic(window))
        {
            ShowWindow(window, SwRestore);
        }
        SetForegroundWindow(window);
    }

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr window);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string className, string windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);
}
