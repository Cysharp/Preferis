using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Profiles;
using Preferis.Settings.Services;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>
/// Warnings about profiles this machine cannot open as configured: a browser that is not installed here, a browser
/// profile it does not have, nothing set for this OS (docs/Design.md §7.4). The configuration may be right for other
/// machines, so these are warnings, each saying what opens instead. Checked against the browsers detected when the
/// settings opened, as it runs on every edit; the Router itself detects again for each URL.
/// </summary>
internal static class ProfileAvailability
{
    public static IEnumerable<ConfigIssue> Check(PreferisConfig config, LocalConfig local, IReadOnlyList<DetectedBrowserInfo> browsers, HostPlatform platform)
    {
        var effective = LocalProfileTargets.Apply(config, local);
        for (var i = 0; i < config.Profiles.Count; i++)
        {
            var isLocal = !ReferenceEquals(effective.Profiles[i], config.Profiles[i]);
            if (Problem(effective.Profiles[i], isLocal, browsers, platform) is { } problem)
            {
                yield return new ConfigIssue(IssueSeverity.Warning, IssueScope.Profile, config.Profiles[i].Id, L("{0}; {1}", problem, Instead(effective, effective.Profiles[i], browsers, platform)));
            }
        }
    }

    /// <summary>Why this machine cannot open <paramref name="profile"/>, or null when it can or the validator already says why not.</summary>
    private static string? Problem(Profile profile, bool isLocal, IReadOnlyList<DetectedBrowserInfo> browsers, HostPlatform platform)
    {
        var os = platform == HostPlatform.MacOS ? "macOS" : "Windows";
        if (profile.Command is { } command)
        {
            if (!string.IsNullOrWhiteSpace(command.For(platform)?.Path))
            {
                return null;
            }
            if (isLocal)
            {
                return L("The command of this computer has no program");
            }
            // Without a program for any OS the validator reports it already.
            var other = platform == HostPlatform.MacOS ? command.Windows : command.MacOS;
            return string.IsNullOrWhiteSpace(other?.Path) ? null : L("The command has no program for {0}", os);
        }

        if (profile.Browser?.For(platform) is not { } browserId)
        {
            if (isLocal)
            {
                return L("No browser is chosen for this computer");
            }
            // Without a browser for any OS the validator reports it already.
            return profile.Browser is null ? null : L("No browser is set for {0}", os);
        }

        if (browsers.FirstOrDefault(b => b.Browser.Id == browserId) is not { } detected)
        {
            return L("{0} is not installed on this computer", BrowserCatalog.Find(browserId)?.DisplayName ?? browserId);
        }

        // Profiles that could not be read are passed through as stored; the browser decides.
        var family = detected.Browser.Family;
        if (profile.BrowserProfile is { } reference && family != BrowserFamily.Other && detected.Profiles is { Count: > 0 } profiles
            && BrowserProfileMatcher.Match(profiles, reference, family) is null)
        {
            return L("“{0}” was not found in {1} on this computer", reference.Name ?? reference.Email ?? reference.Directory, detected.Browser.DisplayName);
        }
        return null;
    }

    /// <summary>What the Router opens instead: the first usable fallback, else the default profile or the picker (docs/Design.md §8).</summary>
    private static string Instead(PreferisConfig config, Profile profile, IReadOnlyList<DetectedBrowserInfo> browsers, HostPlatform platform)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { profile.Id };
        var current = profile;
        while (current.Fallback is { } fallbackId && visited.Add(fallbackId) && config.Profiles.FirstOrDefault(p => p.Id == fallbackId) is { } fallback)
        {
            // The validator's problems (no target at all) make a fallback unusable too.
            if (Problem(fallback, isLocal: true, browsers, platform) is null)
            {
                return L("“{0}” is used instead.", fallback.Name);
            }
            current = fallback;
        }
        return L("the default profile or the picker is used instead.");
    }
}
