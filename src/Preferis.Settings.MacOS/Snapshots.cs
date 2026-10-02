using System.Diagnostics;
using System.Globalization;
using AppKit;
using CoreGraphics;
using Preferis.Settings.MacOS.Controls;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.MacOS;

/// <summary>
/// Developer aid (<c>PREFERIS_SETTINGS_SNAPSHOT=&lt;folder&gt;</c>): captures each page as PNG and quits. The window is
/// placed behind the desktop picture and never activated, so nothing visible changes and the focus stays where it is. Capturing
/// goes through the window server (<c>screencapture -l</c>): drawing the views into a bitmap misses scroll view content
/// on macOS 26. Needs the screen recording permission of the app that started this process.
/// <c>PREFERIS_SETTINGS_SNAPSHOT_APPEARANCE=dark</c> draws the dark appearance.
/// </summary>
internal static class Snapshots
{
    // By page rather than by title, which is in the language of the UI.
    private static string FileName(PageViewModel page) => page switch
    {
        GeneralPageViewModel => "general",
        ProfilesPageViewModel => "profiles",
        RulesPageViewModel => "rules",
        _ => "test",
    };

    public static void TakeAndExit(SettingsWindow window, SettingsViewModel settings, string directory)
    {
        Directory.CreateDirectory(directory);
        var steps = new List<(string Name, Action Prepare)>();
        foreach (var page in settings.Pages.ToList())
        {
            steps.Add((FileName(page), () =>
            {
                settings.SelectedPage = page;
                if (page is TestPageViewModel test && Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT_URL") is { Length: > 0 } url)
                {
                    test.Url = url;
                    test.RunCommand.Execute(null);
                }
            }));
        }
        steps.Add(("rules-third", () =>
        {
            settings.SelectedPage = settings.Rules;
            settings.Rules.SelectedRule = settings.Rules.Rules.ElementAtOrDefault(2);
        }));
        steps.Add(("rules-catch-all", () => settings.Rules.SelectCatchAllCommand.Execute(null)));
        steps.Add(("profiles-third", () =>
        {
            settings.SelectedPage = settings.Profiles;
            settings.Profiles.SelectedProfile = settings.Profiles.Profiles.ElementAtOrDefault(2) ?? settings.Profiles.SelectedProfile;
        }));
        // The end of the same editor: this computer's target and the Advanced section.
        steps.Add(("profiles-third-end", () => ScrollToEnd(window.Window.ContentView!)));

        // Behind the desktop picture: on screen for the window server (which captures by window id regardless of what
        // covers it), invisible to the person using the Mac. Off-screen windows cannot be captured at all.
        const int BelowDesktopLevel = -2147483630;
        var nsWindow = window.Window;
        if (Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT_APPEARANCE") == "dark")
        {
            nsWindow.Appearance = NSAppearance.GetAppearance(NSAppearance.NameDarkAqua);
        }
        nsWindow.Level = (NSWindowLevel)BelowDesktopLevel;
        nsWindow.OrderFrontRegardless();

        var index = 0;
        NSTimer.CreateRepeatingScheduledTimer(0.5, timer =>
        {
            if (index > 0)
            {
                Capture(nsWindow, Path.Combine(directory, $"{steps[index - 1].Name}.png"));
            }
            if (index == steps.Count)
            {
                timer.Invalidate();
                NSApplication.SharedApplication.Terminate(NSApplication.SharedApplication);
                return;
            }
            steps[index++].Prepare();
        });
    }

    // Scrolls the pages (the scroll views of Ui.ScrollPage) to their bottom.
    private static void ScrollToEnd(NSView view)
    {
        if (view is NSScrollView { DocumentView: FlippedView document } scroll)
        {
            document.ScrollPoint(new CGPoint(0, Math.Max(0, (double)(document.Frame.Height - scroll.ContentView.Bounds.Height))));
        }
        foreach (var child in view.Subviews)
        {
            ScrollToEnd(child);
        }
    }

    private static void Capture(NSWindow window, string path)
    {
        var info = new ProcessStartInfo("/usr/sbin/screencapture") { UseShellExecute = false };
        foreach (var argument in new[] { "-x", "-o", "-l", window.WindowNumber.ToString(CultureInfo.InvariantCulture), path })
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info);
        process?.WaitForExit(10_000);
    }
}
