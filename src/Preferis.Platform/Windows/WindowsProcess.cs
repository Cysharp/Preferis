using System.Runtime.Versioning;

namespace Preferis.Platform.Windows;

/// <summary>Process-wide Windows matters of the Router: its console and the right to bring windows to the front.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsProcess
{
    private const int AttachParentProcess = -1;
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const uint FileTypeUnknown = 0;
    private const uint HandleFlagInherit = 1;

    /// <summary>
    /// <c>Preferis.exe</c> is a GUI program so that no console window flashes up when a link is clicked. Run from a
    /// terminal, it borrows the terminal's console for the output of its commands. Output that is redirected (a pipe, as
    /// when the settings run it) is left alone.
    /// </summary>
    public static void AttachToParentConsole()
    {
        var output = Native.GetStdHandle(StdOutputHandle);
        if (output != IntPtr.Zero && output != -1 && Native.GetFileType(output) != FileTypeUnknown)
        {
            return;
        }
        Native.AttachConsole(AttachParentProcess);
    }

    /// <summary>
    /// Keeps the Router's standard handles out of the programs it starts. Run by the settings, they are pipes the settings
    /// read to the end: a browser that inherited one would hold it open, and the reader waiting, for as long as it runs.
    /// </summary>
    public static void KeepStandardHandlesFromChildren()
    {
        foreach (var id in (int[])[StdInputHandle, StdOutputHandle, StdErrorHandle])
        {
            var handle = Native.GetStdHandle(id);
            if (handle != IntPtr.Zero && handle != -1)
            {
                Native.SetHandleInformation(handle, HandleFlagInherit, 0);
            }
        }
    }

    /// <summary>
    /// Lets <paramref name="processId"/> bring its window to the front. Started by the shell, the Router may do so itself;
    /// a child (the picker, the settings) may not unless handed the right (docs/Platform.md §2.6).
    /// </summary>
    public static bool AllowForeground(int processId) => Native.AllowSetForegroundWindow(processId);

    /// <summary>Hands the right to whichever process asks next, for one that is not known yet (a running settings window).</summary>
    public static bool AllowAnyForeground() => Native.AllowSetForegroundWindow(Native.AsfwAny);
}
