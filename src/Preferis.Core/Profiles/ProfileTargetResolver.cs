using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Profiles;

/// <summary>
/// Resolves a profile's target and fallback chain without a URL or launch plan. Routing supplies live browser services;
/// settings supplies the detection snapshot it already has, so checking availability never needs another OS lookup.
/// </summary>
public sealed class ProfileTargetResolver(HostPlatform platform, IBrowserLocator locator, IBrowserProfileEnumerator profiles)
{
    public ProfileTargetResolution ResolveWithFallback(PreferisConfig config, string profileId)
    {
        var warnings = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { profileId };
        var currentId = profileId;

        while (true)
        {
            var profile = config.Profiles.FirstOrDefault(p => p.Id == currentId);
            if (profile is null)
            {
                return new ProfileTargetResolution.Unresolved(ProfileTargetFailure.ProfileMissing, L("Profile '{0}' does not exist.", currentId));
            }

            switch (Resolve(profile))
            {
                case ProfileTargetResolution.Resolved resolved:
                    return resolved with { Warnings = [.. warnings, .. resolved.Warnings] };
                case ProfileTargetResolution.Unresolved unresolved when profile.Fallback is { } fallback && visited.Add(fallback):
                    warnings.Add(L("Profile {0} could not be resolved ({1}); using fallback {2}.", ProfileNames.Describe(profile), unresolved.Reason, ProfileNames.Describe(config, fallback)));
                    currentId = fallback;
                    continue;
                case ProfileTargetResolution.Unresolved unresolved:
                    return unresolved;
            }
        }
    }

    public ProfileTargetResolution Resolve(Profile profile)
    {
        if (profile.Command is { } command)
        {
            return command.For(platform) is { } spec && !string.IsNullOrWhiteSpace(spec.Path)
                ? new ProfileTargetResolution.Resolved(profile, new ResolvedProfileTarget.Command(spec), [])
                : new ProfileTargetResolution.Unresolved(ProfileTargetFailure.CommandMissingForPlatform, L("Profile {0} has no command for {1}.", ProfileNames.Describe(profile), platform));
        }

        if (profile.Browser is null)
        {
            return new ProfileTargetResolution.Unresolved(ProfileTargetFailure.TargetMissing, L("Profile {0} is not set on this computer.", ProfileNames.Describe(profile)));
        }
        if (profile.Browser.For(platform) is not { } browserId)
        {
            return new ProfileTargetResolution.Unresolved(ProfileTargetFailure.BrowserMissingForPlatform, L("Profile {0} has no browser or command for {1}.", ProfileNames.Describe(profile), platform));
        }
        if (locator.TryResolve(browserId) is not { } browser)
        {
            return new ProfileTargetResolution.Unresolved(ProfileTargetFailure.BrowserNotInstalled, L("Browser '{0}' is not installed.", browserId));
        }

        if (profile.BrowserProfile is not { } reference || browser.Family is not (BrowserFamily.Chromium or BrowserFamily.Firefox))
        {
            return Browser(profile, browser, null);
        }

        if (browser.Family == BrowserFamily.Firefox)
        {
            var requested = reference.Name ?? reference.Directory;
            if (string.IsNullOrEmpty(requested))
            {
                return new ProfileTargetResolution.Unresolved(ProfileTargetFailure.BrowserProfileNameMissing, L("Browser profile for '{0}' has no name.", browser.Id));
            }
            var available = profiles.Enumerate(browser);
            if (available is null || available.Count == 0)
            {
                return Browser(profile, browser, requested, [L("Profile data of '{0}' could not be read; using the stored name '{1}'.", browser.Id, requested)]);
            }
            return BrowserProfileMatcher.Match(available, reference, BrowserFamily.Firefox)?.Name is { } name
                ? Browser(profile, browser, name)
                : MissingBrowserProfile(reference, browser);
        }

        var chromiumProfiles = profiles.Enumerate(browser);
        if (chromiumProfiles is null || chromiumProfiles.Count == 0)
        {
            return !string.IsNullOrEmpty(reference.Directory)
                ? Browser(profile, browser, reference.Directory, [L("Profile data of '{0}' could not be read; using the stored directory '{1}'.", browser.Id, reference.Directory)])
                : new ProfileTargetResolution.Unresolved(ProfileTargetFailure.BrowserProfileDataUnavailable, L("Profile data of '{0}' could not be read and no profile directory is stored.", browser.Id));
        }
        return BrowserProfileMatcher.Match(chromiumProfiles, reference, BrowserFamily.Chromium) is { } matched
            ? Browser(profile, browser, matched.Directory)
            : MissingBrowserProfile(reference, browser);
    }

    private static ProfileTargetResolution.Resolved Browser(Profile profile, DetectedBrowser browser, string? directory, IReadOnlyList<string>? warnings = null)
        => new(profile, new ResolvedProfileTarget.Browser(browser, directory), warnings ?? []);

    private static ProfileTargetResolution.Unresolved MissingBrowserProfile(BrowserProfileRef reference, DetectedBrowser browser)
        => new(ProfileTargetFailure.BrowserProfileNotFound, L("Browser profile {0} was not found in '{1}'.", Describe(reference), browser.Id));

    private static string Describe(BrowserProfileRef reference)
        => "(" + string.Join(", ", new[] { ("email", reference.Email), ("name", reference.Name), ("directory", reference.Directory) }
            .Where(p => !string.IsNullOrEmpty(p.Item2)).Select(p => $"{p.Item1}={p.Item2}")) + ")";
}
