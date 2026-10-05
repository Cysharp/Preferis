using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Profiles;
using Preferis.Settings.Services;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>
/// Warnings about profiles this machine cannot open as configured: a browser that is not installed here, a browser
/// profile it does not have, nothing set for this OS or, for a profile set on each computer, for this computer
/// (docs/Design.md §7.4). The configuration may be right for other
/// machines, so these are warnings, each saying what opens instead. Checked against the browsers detected when the
/// settings opened, as it runs on every edit; the Router itself detects again for each URL.
/// </summary>
internal static class ProfileAvailability
{
    public static IEnumerable<ConfigIssue> Check(PreferisConfig config, LocalConfig local, IReadOnlyList<DetectedBrowserInfo> browsers, HostPlatform platform)
    {
        var effective = LocalProfileTargets.Apply(config, local);
        var snapshot = new BrowserSnapshot(browsers);
        var resolver = new ProfileTargetResolver(platform, snapshot, snapshot);
        for (var i = 0; i < config.Profiles.Count; i++)
        {
            var profile = effective.Profiles[i];
            var isLocal = !ReferenceEquals(effective.Profiles[i], config.Profiles[i]);
            if (resolver.Resolve(profile) is ProfileTargetResolution.Unresolved unresolved
                && Describe(unresolved, profile, isLocal, snapshot, platform) is { } problem)
            {
                var instead = resolver.ResolveWithFallback(effective, profile.Id) is ProfileTargetResolution.Resolved fallback
                    ? L("“{0}” is used instead.", fallback.Profile.Name)
                    : L("the default profile or the picker is used instead.");
                yield return new ConfigIssue(IssueSeverity.Warning, IssueScope.Profile, config.Profiles[i].Id, L("{0}; {1}", problem, instead));
            }
        }
    }

    /// <summary>Explains a Core resolution failure in the editor, omitting an error the validator already displays.</summary>
    private static string? Describe(ProfileTargetResolution.Unresolved unresolved, Profile profile, bool isLocal, BrowserSnapshot browsers, HostPlatform platform)
    {
        var os = platform == HostPlatform.MacOS ? "macOS" : "Windows";
        switch (unresolved.Failure)
        {
            case ProfileTargetFailure.CommandMissingForPlatform:
                if (isLocal)
                {
                    return L("The command of this computer has no program");
                }
                // A command with no program on either OS is already a validation error, not another warning.
                var other = platform == HostPlatform.MacOS ? profile.Command?.Windows : profile.Command?.MacOS;
                return string.IsNullOrWhiteSpace(other?.Path) ? null : L("The command has no program for {0}", os);
            case ProfileTargetFailure.TargetMissing:
                return isLocal ? L("No browser is chosen for this computer") : L("Not set on this computer");
            case ProfileTargetFailure.BrowserMissingForPlatform:
                return isLocal ? L("No browser is chosen for this computer") : L("No browser is set for {0}", os);
            case ProfileTargetFailure.BrowserNotInstalled:
                var browserId = profile.Browser!.For(platform)!;
                return L("{0} is not installed on this computer", BrowserCatalog.Find(browserId)?.DisplayName ?? browserId);
            case ProfileTargetFailure.BrowserProfileNotFound:
                var reference = profile.BrowserProfile!;
                var browser = browsers.TryResolve(profile.Browser!.For(platform)!)!;
                return L("“{0}” was not found in {1} on this computer", reference.Name ?? reference.Email ?? reference.Directory, browser.DisplayName);
            default:
                return unresolved.Reason;
        }
    }

    /// <summary>No live platform services: every check uses only the browsers and profiles detected for this session.</summary>
    private sealed class BrowserSnapshot(IReadOnlyList<DetectedBrowserInfo> browsers) : IBrowserLocator, IBrowserProfileEnumerator
    {
        public DetectedBrowser? TryResolve(string browserId) => browsers.FirstOrDefault(b => b.Browser.Id == browserId)?.Browser;

        public IReadOnlyList<BrowserProfileInfo>? Enumerate(DetectedBrowser browser) => browsers.FirstOrDefault(b => b.Browser.Id == browser.Id)?.Profiles;
    }
}
