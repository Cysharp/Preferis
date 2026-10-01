using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Views;

/// <summary>
/// Developer aid (<c>PREFERIS_SETTINGS_SNAPSHOT=&lt;folder&gt;</c>): renders every page to PNG and quits, for checking
/// layouts without screen recording rights or a visible display.
/// </summary>
internal static class Snapshots
{
    public static void TakeAndExit(Window window, SettingsViewModel settings, string directory, IClassicDesktopStyleApplicationLifetime desktop)
    {
        window.Opened += async (_, _) =>
        {
            Directory.CreateDirectory(directory);
            foreach (var page in settings.Pages.ToList())
            {
                settings.SelectedPage = page;
                if (page is TestPageViewModel test && Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT_URL") is { Length: > 0 } url)
                {
                    test.Url = url;
                    test.RunCommand.Execute(null);
                }
                await Task.Delay(500);
                Save(window, Path.Combine(directory, $"{page.Title.ToLowerInvariant()}.png"));
            }

            if (settings.Rules.Rules.Count > 0)
            {
                settings.SelectedPage = settings.Rules;
                settings.Rules.SelectedRule = settings.Rules.Rules[^1];
                await Task.Delay(500);
                Save(window, Path.Combine(directory, "rules-last.png"));
                settings.Rules.SelectCatchAllCommand.Execute(null);
                await Task.Delay(500);
                Save(window, Path.Combine(directory, "rules-catch-all.png"));
            }
            desktop.Shutdown();
        };
    }

    private static void Save(Window window, string path)
    {
        var scale = window.RenderScaling;
        var size = new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
