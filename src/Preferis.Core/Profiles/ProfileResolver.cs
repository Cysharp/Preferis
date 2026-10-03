using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Profiles;

/// <summary>
/// Turns a configured <see cref="Profile"/> into a <see cref="LaunchPlan"/> for this machine.
/// Browser lookups go through the injected <see cref="IBrowserLocator"/> so that only the browser actually needed is detected.
/// See docs/Design.md §7.
/// </summary>
public sealed class ProfileResolver
{
    private const string UrlPlaceholder = "{url}";

    private readonly HostPlatform _platform;
    private readonly IBrowserLocator _locator;
    private readonly IBrowserProfileEnumerator _profiles;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _homeDirectory;

    public ProfileResolver(HostPlatform platform, IBrowserLocator locator, IBrowserProfileEnumerator profiles, Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        _platform = platform;
        _locator = locator;
        _profiles = profiles;
        _getEnvironmentVariable = getEnvironmentVariable;
        _homeDirectory = homeDirectory;
    }

    /// <summary>Resolves a profile by id, following <see cref="Profile.Fallback"/> links until one resolves.</summary>
    public ResolveResult ResolveWithFallback(PreferisConfig config, string profileId, RoutingUrl url)
    {
        var warnings = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { profileId };
        var currentId = profileId;

        while (true)
        {
            var profile = config.Profiles.FirstOrDefault(p => p.Id == currentId);
            if (profile is null)
            {
                return new ResolveResult.Unresolved(L("Profile '{0}' does not exist.", currentId));
            }

            switch (Resolve(profile, url))
            {
                case ResolveResult.Resolved resolved:
                    return new ResolveResult.Resolved(resolved.Plan, [.. warnings, .. resolved.Warnings], currentId);
                case ResolveResult.Unresolved unresolved when profile.Fallback is { } fallback && visited.Add(fallback):
                    warnings.Add(L("Profile {0} could not be resolved ({1}); using fallback {2}.", ProfileNames.Describe(profile), unresolved.Reason, ProfileNames.Describe(config, fallback)));
                    currentId = fallback;
                    continue;
                case ResolveResult.Unresolved unresolved:
                    return unresolved;
            }
        }
    }

    public ResolveResult Resolve(Profile profile, RoutingUrl url)
    {
        if (profile.Command is not null)
        {
            return ResolveCommand(profile, url);
        }

        if (profile.Browser is null)
        {
            // Set on each computer (docs/Design.md §7.4), and not on this one: neither shared nor in local.json.
            return new ResolveResult.Unresolved(L("Profile {0} is not set on this computer.", ProfileNames.Describe(profile)));
        }

        var browserId = profile.Browser.For(_platform);
        if (browserId is null)
        {
            return new ResolveResult.Unresolved(L("Profile {0} has no browser or command for {1}.", ProfileNames.Describe(profile), _platform));
        }

        var browser = _locator.TryResolve(browserId);
        if (browser is null)
        {
            return new ResolveResult.Unresolved(L("Browser '{0}' is not installed.", browserId));
        }

        var warnings = new List<string>();
        var arguments = new List<string>();
        string? profileDirectory = null;

        switch (browser.Family)
        {
            case BrowserFamily.Chromium:
                if (profile.BrowserProfile is { } chromiumRef)
                {
                    var (directory, error) = ResolveChromiumProfile(chromiumRef, browser, warnings);
                    if (directory is null)
                    {
                        return new ResolveResult.Unresolved(error!);
                    }
                    profileDirectory = directory;
                    arguments.Add($"--profile-directory={directory}");
                }
                break;

            case BrowserFamily.Firefox:
                if (profile.BrowserProfile is { } firefoxRef)
                {
                    var (name, error) = ResolveFirefoxProfile(firefoxRef, browser, warnings);
                    if (name is null)
                    {
                        return new ResolveResult.Unresolved(error!);
                    }
                    profileDirectory = name;
                    arguments.Add("-P");
                    arguments.Add(name);
                }
                break;

            default:
                if (profile.BrowserProfile is not null)
                {
                    warnings.Add(L("Browser '{0}' does not support selecting a browser profile; ignoring it.", browser.Id));
                }
                break;
        }

        if (profile.Private)
        {
            var flag = browser.CatalogEntry?.PrivateModeArgument ?? DefaultPrivateFlag(browser.Family);
            if (flag is null)
            {
                warnings.Add(L("Browser '{0}' does not support private mode; ignoring it.", browser.Id));
            }
            else
            {
                arguments.Add(flag);
            }
        }

        if (browser.Family == BrowserFamily.Other && _platform == HostPlatform.MacOS)
        {
            // Safari and unknown macOS apps do not take URLs on their command line; let Launch Services open it.
            if (profile.ExtraArgs is { Count: > 0 })
            {
                warnings.Add(L("Browser '{0}' is opened through the OS; extra arguments are ignored.", browser.Id));
            }
            return new ResolveResult.Resolved(
                new LaunchPlan(LaunchKind.OpenWithApplication, browser.ApplicationPath ?? browser.ExecutablePath, [url.AbsoluteUri], browser, null),
                warnings,
                profile.Id);
        }

        if (profile.ExtraArgs is not null)
        {
            arguments.AddRange(profile.ExtraArgs);
        }
        arguments.Add(url.AbsoluteUri);

        return new ResolveResult.Resolved(new LaunchPlan(LaunchKind.Execute, browser.ExecutablePath, arguments, browser, profileDirectory), warnings, profile.Id);
    }

    /// <summary>The plan for opening a URL with a detected browser without any profile or option (fallback picker).</summary>
    public static LaunchPlan PlanForBrowser(DetectedBrowser browser, RoutingUrl url, HostPlatform platform)
        => browser.Family == BrowserFamily.Other && platform == HostPlatform.MacOS
            ? new LaunchPlan(LaunchKind.OpenWithApplication, browser.ApplicationPath ?? browser.ExecutablePath, [url.AbsoluteUri], browser, null)
            : new LaunchPlan(LaunchKind.Execute, browser.ExecutablePath, [url.AbsoluteUri], browser, null);

    private ResolveResult ResolveCommand(Profile profile, RoutingUrl url)
    {
        var spec = profile.Command!.For(_platform);
        if (spec is null || string.IsNullOrWhiteSpace(spec.Path))
        {
            return new ResolveResult.Unresolved(L("Profile {0} has no command for {1}.", ProfileNames.Describe(profile), _platform));
        }

        var path = EnvironmentExpander.Expand(spec.Path, _platform, _getEnvironmentVariable, _homeDirectory);
        var arguments = spec.Args.Select(a => a.Replace(UrlPlaceholder, url.AbsoluteUri, StringComparison.Ordinal)).ToList();
        if (!spec.Args.Any(a => a.Contains(UrlPlaceholder, StringComparison.Ordinal)))
        {
            arguments.Add(url.AbsoluteUri);
        }

        return new ResolveResult.Resolved(new LaunchPlan(LaunchKind.Execute, path, arguments, null, null), [], profile.Id);
    }

    // Resolution order is email, name, directory (docs/Design.md §7.2). When the profile list cannot be read
    // (e.g. macOS 27 denies access), the stored directory is passed through unchanged.
    private (string? Directory, string? Error) ResolveChromiumProfile(BrowserProfileRef reference, DetectedBrowser browser, List<string> warnings)
    {
        var profiles = _profiles.Enumerate(browser);
        if (profiles is null || profiles.Count == 0)
        {
            if (!string.IsNullOrEmpty(reference.Directory))
            {
                warnings.Add(L("Profile data of '{0}' could not be read; using the stored directory '{1}'.", browser.Id, reference.Directory));
                return (reference.Directory, null);
            }
            return (null, L("Profile data of '{0}' could not be read and no profile directory is stored.", browser.Id));
        }

        var match = BrowserProfileMatcher.Match(profiles, reference, BrowserFamily.Chromium);

        return match is null
            ? (null, L("Browser profile {0} was not found in '{1}'.", Describe(reference), browser.Id))
            : (match.Directory, null);
    }

    private (string? Name, string? Error) ResolveFirefoxProfile(BrowserProfileRef reference, DetectedBrowser browser, List<string> warnings)
    {
        var requested = reference.Name ?? reference.Directory;
        if (string.IsNullOrEmpty(requested))
        {
            return (null, L("Browser profile for '{0}' has no name.", browser.Id));
        }

        var profiles = _profiles.Enumerate(browser);
        if (profiles is null || profiles.Count == 0)
        {
            warnings.Add(L("Profile data of '{0}' could not be read; using the stored name '{1}'.", browser.Id, requested));
            return (requested, null);
        }

        var match = BrowserProfileMatcher.Match(profiles, reference, BrowserFamily.Firefox);
        return match?.Name is null
            ? (null, L("Browser profile {0} was not found in '{1}'.", Describe(reference), browser.Id))
            : (match.Name, null);
    }

    private static string Describe(BrowserProfileRef reference)
        => "(" + string.Join(", ", new[] { ("email", reference.Email), ("name", reference.Name), ("directory", reference.Directory) }
            .Where(p => !string.IsNullOrEmpty(p.Item2)).Select(p => $"{p.Item1}={p.Item2}")) + ")";

    private static string? DefaultPrivateFlag(BrowserFamily family) => family switch
    {
        BrowserFamily.Chromium => "--incognito",
        BrowserFamily.Firefox => "-private-window",
        _ => null,
    };
}
