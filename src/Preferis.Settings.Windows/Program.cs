using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Preferis.Settings.Localization;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Windows;

internal static class Program
{
    /// <summary>The window title, also how a second start finds the running window.</summary>
    public static string Title => L("Preferis Settings");

    public static bool IsSnapshot => Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT") is { Length: > 0 };

    [STAThread]
    private static int Main()
    {
        // One window edits the files; a second start (the Router's --settings, the Start menu) brings it forward instead.
        using var instance = new Mutex(initiallyOwned: true, @"Local\Preferis.Settings", out var isFirst);
        if (!isFirst && !IsSnapshot)
        {
            // The running window may show another language than this start would, after the language was changed.
            Win32.ActivateWindow(SettingsText.InEveryLanguage("Preferis Settings"));
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
