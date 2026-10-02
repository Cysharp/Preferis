using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Preferis.Settings.Windows.Controls;
using Preferis.Settings.ViewModels;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Preferis.Settings.Windows;

/// <summary>
/// Developer aid (<c>PREFERIS_SETTINGS_SNAPSHOT=&lt;folder&gt;</c>): renders each page to PNG and quits. The window is
/// shown off screen without being activated, so nothing visible changes and the focus stays where it is.
/// <c>PREFERIS_SETTINGS_SNAPSHOT_APPEARANCE=dark</c> draws the dark theme, <c>PREFERIS_SETTINGS_SNAPSHOT_URL</c> tests that
/// URL on the test page, <c>PREFERIS_SETTINGS_SNAPSHOT_WIDTH</c> sets the window width (DIP) and
/// <c>PREFERIS_SETTINGS_SNAPSHOT_CONFIG</c> reads the configuration from that folder instead of this machine's.
/// Mica is drawn by the compositor and does not appear; the theme's plain background stands in for it.
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
        // The end of the long profile editor: this computer's target and the fallback.
        steps.Add(("profiles-third-end", () =>
        {
            foreach (var viewer in Descendants(window.Root).OfType<ScrollViewer>().Where(v => v.ScrollableHeight > 0))
            {
                viewer.ChangeView(null, viewer.ScrollableHeight, null, disableAnimation: true);
            }
        }));

        var root = window.Root;
        root.Background = Ui.Brush("SolidBackgroundFillColorBaseBrush");
        if (Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT_APPEARANCE") == "dark")
        {
            root.RequestedTheme = ElementTheme.Dark;
        }
        var appWindow = window.Window.AppWindow;
        var size = window.PhysicalDefaultSize();
        if (double.TryParse(Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT_WIDTH"), System.Globalization.CultureInfo.InvariantCulture, out var width))
        {
            // Device independent pixels, like DefaultSize: for checking narrow windows.
            size.Width = (int)(size.Width * width / SettingsWindow.DefaultSize.Width);
        }
        appWindow.MoveAndResize(new RectInt32(-32000, -32000, size.Width, size.Height));
        appWindow.Show(activateWindow: false);

        var index = 0;
        var timer = window.Window.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(700);
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (index > 0)
            {
                await Capture(root, Path.Combine(directory, $"{steps[index - 1].Name}.png"));
            }
            if (index == steps.Count)
            {
                Application.Current.Exit();
                return;
            }
            steps[index++].Prepare();
            timer.Start();
        };
        timer.Start();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task Capture(UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
        await using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }
}
