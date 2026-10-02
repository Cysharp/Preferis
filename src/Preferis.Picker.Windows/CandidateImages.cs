using Microsoft.UI.Xaml.Media.Imaging;
using Preferis.Core.Configuration;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Platform.Browsers;
using Preferis.Platform.Windows;

namespace Preferis.Picker.Windows;

/// <summary>
/// The browser icon and the browser profile picture of each candidate, resolved from the identifiers in the request.
/// Images never travel over the IPC (docs/Design.md §5.2); the picker looks them up through the Platform layer.
/// </summary>
/// <remarks>
/// Reading the registry, the icons and Local State takes about as long as starting WinUI, so it runs on a thread of its
/// own from the start of the process. Only turning the result into images needs the UI thread.
/// </remarks>
internal sealed class CandidateImages
{
    // Twice the displayed size, so that icons stay sharp on 200 % displays.
    private const int IconPixels = 64;

    private readonly Task<IReadOnlyList<Found>> _found;

    private CandidateImages(Task<IReadOnlyList<Found>> found)
    {
        _found = found;
    }

    public static CandidateImages StartLoading(PickerRequest request) => new(Task.Run(() => Load(request)));

    /// <summary>The browser's icon, or null for commands and browsers that are not installed here (shown as a glyph).</summary>
    public BitmapImage? Icon(int candidate)
        => Get(candidate)?.IconPng is { } png ? Images.FromPng(png) : null;

    public BitmapImage? Avatar(int candidate)
        => Get(candidate)?.AvatarPath is { } path ? new BitmapImage(new Uri(path)) { DecodePixelWidth = IconPixels / 2 } : null;

    private Found? Get(int candidate)
    {
        try
        {
            var found = _found.GetAwaiter().GetResult();
            return candidate < found.Count ? found[candidate] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pictures are a nicety; the picker works without them.
            return null;
        }
    }

    private static IReadOnlyList<Found> Load(PickerRequest request)
    {
        // Overrides from local.json point at portable or unusual installs; without them those icons would be missing.
        var detector = new WindowsBrowserDetector(ConfigLoader.ForCurrentMachine().LoadLocal().BrowserOverrides);
        var enumerator = new BrowserProfileEnumerator();
        var browsers = request.Candidates
            .Select(c => c.Browser)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, detector.TryResolve, StringComparer.Ordinal);
        var icons = browsers.Values
            .OfType<Core.Browsers.DetectedBrowser>()
            .ToDictionary(b => b.Id, b => WindowsApplicationIcons.GetPng(b.ExecutablePath, IconPixels), StringComparer.Ordinal);
        var profiles = new Dictionary<string, IReadOnlyList<Core.Browsers.BrowserProfileInfo>?>(StringComparer.Ordinal);

        return request.Candidates.Select(candidate =>
        {
            if (candidate.Browser is not { } id || browsers[id] is not { } browser)
            {
                return new Found(null, null);
            }
            string? avatar = null;
            if (candidate.BrowserProfile is { } reference)
            {
                if (!profiles.TryGetValue(id, out var list))
                {
                    list = enumerator.Enumerate(browser);
                    profiles[id] = list;
                }
                var path = list is null ? null : BrowserProfileMatcher.Match(list, reference, browser.Family)?.AvatarPath;
                avatar = path is not null && File.Exists(path) ? path : null;
            }
            return new Found(icons.GetValueOrDefault(id), avatar);
        }).ToList();
    }

    private sealed record Found(byte[]? IconPng, string? AvatarPath);
}

internal static class Images
{
    public static BitmapImage FromPng(byte[] png)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.SetSource(stream.AsRandomAccessStream());
        return image;
    }
}
