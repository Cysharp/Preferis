using AppKit;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Platform.Browsers;
using Preferis.Platform.MacOS;

namespace Preferis.Picker.MacOS;

/// <summary>
/// Resolves the browser icon and the browser profile picture of a candidate from the identifiers in the request.
/// Images never travel over the IPC (docs/Design.md §5.2); the picker looks them up through the Platform layer.
/// </summary>
internal sealed class CandidateImages
{
    private readonly MacBrowserDetector _detector;
    private readonly BrowserProfileEnumerator _profiles = new();
    private readonly Dictionary<string, DetectedBrowser?> _browsers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<BrowserProfileInfo>?> _profileLists = new(StringComparer.Ordinal);

    public CandidateImages()
    {
        // Overrides from local.json point at portable or unusual installs; without them those icons would be missing.
        var local = ConfigLoader.ForCurrentMachine().LoadLocal();
        _detector = new MacBrowserDetector(local.BrowserOverrides, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public NSImage Icon(PickerCandidate candidate)
    {
        if (Browser(candidate)?.ApplicationPath is { } app)
        {
            return NSWorkspace.SharedWorkspace.IconForFile(app);
        }
        // Commands have no browser; a browser that is not installed here gets a neutral globe.
        return NSImage.GetSystemSymbol(candidate.Browser is null ? "terminal" : "globe", null) ?? NSImage.ImageNamed(NSImageName.ApplicationIcon)!;
    }

    public NSImage? Avatar(PickerCandidate candidate)
    {
        if (candidate.BrowserProfile is not { } reference || Browser(candidate) is not { } browser)
        {
            return null;
        }

        if (!_profileLists.TryGetValue(browser.Id, out var profiles))
        {
            profiles = _profiles.Enumerate(browser);
            _profileLists[browser.Id] = profiles;
        }

        var path = profiles is null ? null : BrowserProfileMatcher.Match(profiles, reference, browser.Family)?.AvatarPath;
        return path is not null && File.Exists(path) ? new NSImage(path) : null;
    }

    private DetectedBrowser? Browser(PickerCandidate candidate)
    {
        if (candidate.Browser is not { } id)
        {
            return null;
        }
        if (!_browsers.TryGetValue(id, out var browser))
        {
            browser = _detector.TryResolve(id);
            _browsers[id] = browser;
        }
        return browser;
    }
}
