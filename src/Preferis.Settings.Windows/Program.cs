using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Preferis.Settings.Windows;

internal static class Program
{
    /// <summary>The window title, also how a second start finds the running window.</summary>
    public const string Title = "Preferis Settings";

    public static bool IsSnapshot => Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT") is { Length: > 0 };

    [STAThread]
    private static int Main()
    {
        // One window edits the files; a second start (the Router's --settings, the Start menu) brings it forward instead.
        using var instance = new Mutex(initiallyOwned: true, @"Local\Preferis.Settings", out var isFirst);
        if (!isFirst && !IsSnapshot)
        {
            Win32.ActivateWindow(Title);
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }
}
