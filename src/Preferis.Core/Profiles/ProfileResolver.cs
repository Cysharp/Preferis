using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;

namespace Preferis.Core.Profiles;

/// <summary>
/// Turns a configured <see cref="Profile"/> into a <see cref="LaunchPlan"/> for this machine.
/// Browser lookups go through the injected <see cref="IBrowserLocator"/> so that only the browser actually needed is detected.
/// See docs/Design.md §7.
/// </summary>
public sealed class ProfileResolver
{
    private const string UrlPlaceholder = "{url}";

    private readonly Platform _platform;
    private readonly IBrowserLocator _locator;
    private readonly IBrowserProfileEnumerator _profiles;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _homeDirectory;

    public ProfileResolver(Platform platform, IBrowserLocator locator, IBrowserProfileEnumerator profiles, Func<string, string?> getEnvironmentVariable, string homeDirectory)
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
                return new ResolveResult.Unresolved($"Profile '{currentId}' does not exist.");
            }

            switch (Resolve(profile, url))
            {
                case ResolveResult.Resolved resolved:
                    return new ResolveResult.Resolved(resolved.Plan, [.. warnings, .. resolved.Warnings], currentId);
                case ResolveResult.Unresolved unresolved when profile.Fallback is { } fallback && visited.Add(fallback):
                    warnings.Add($"Profile '{currentId}' could not be resolved ({unresolved.Reason}); using fallback '{fallback}'.");
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

        var browserId = profile.Browser?.For(_platform);
        if (browserId is null)
        {
            return new ResolveResult.Unresolved($"Profile '{profile.Id}' has no browser or command for {_platform}.");
        }

        var browser = _locator.TryResolve(browserId);
        if (browser is null)
        {
            return new ResolveResult.Unresolved($"Browser '{browserId}' is not installed.");
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
                    warnings.Add($"Browser '{browser.Id}' does not support selecting a browser profile; ignoring it.");
                }
                break;
        }

        if (profile.Private)
        {
            var flag = browser.CatalogEntry?.PrivateModeArgument ?? DefaultPrivateFlag(browser.Family);
            if (flag is null)
            {
                warnings.Add($"Browser '{browser.Id}' has no known private mode flag; ignoring it.");
            }
            else
            {
                arguments.Add(flag);
            }
        }

        if (browser.Family == BrowserFamily.Other && _platform == Platform.MacOS)
        {
            // Safari and unknown macOS apps do not take URLs on their command line; let Launch Services open it.
            if (profile.ExtraArgs is { Count: > 0 })
            {
                warnings.Add($"Browser '{browser.Id}' is opened through the OS; extra arguments are ignored.");
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
    public static LaunchPlan PlanForBrowser(DetectedBrowser browser, RoutingUrl url, Platform platform)
        => browser.Family == BrowserFamily.Other && platform == Platform.MacOS
            ? new LaunchPlan(LaunchKind.OpenWithApplication, browser.ApplicationPath ?? browser.ExecutablePath, [url.AbsoluteUri], browser, null)
            : new LaunchPlan(LaunchKind.Execute, browser.ExecutablePath, [url.AbsoluteUri], browser, null);

    private ResolveResult ResolveCommand(Profile profile, RoutingUrl url)
    {
        var spec = profile.Command!.For(_platform);
        if (spec is null || string.IsNullOrWhiteSpace(spec.Path))
        {
            return new ResolveResult.Unresolved($"Profile '{profile.Id}' has no command for {_platform}.");
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
                warnings.Add($"Profile data of '{browser.Id}' could not be read; using the stored directory '{reference.Directory}'.");
                return (reference.Directory, null);
            }
            return (null, $"Profile data of '{browser.Id}' could not be read and no profile directory is stored.");
        }

        var match =
            FindBy(profiles, p => p.Email, reference.Email, StringComparison.OrdinalIgnoreCase)
            ?? FindBy(profiles, p => p.Name, reference.Name, StringComparison.OrdinalIgnoreCase)
            ?? FindBy(profiles, p => p.Directory, reference.Directory, StringComparison.Ordinal);

        return match is null
            ? (null, $"Browser profile {Describe(reference)} was not found in '{browser.Id}'.")
            : (match.Directory, null);
    }

    private (string? Name, string? Error) ResolveFirefoxProfile(BrowserProfileRef reference, DetectedBrowser browser, List<string> warnings)
    {
        var requested = reference.Name ?? reference.Directory;
        if (string.IsNullOrEmpty(requested))
        {
            return (null, $"Browser profile for '{browser.Id}' has no name.");
        }

        var profiles = _profiles.Enumerate(browser);
        if (profiles is null || profiles.Count == 0)
        {
            warnings.Add($"Profile data of '{browser.Id}' could not be read; using the stored name '{requested}'.");
            return (requested, null);
        }

        var match = FindBy(profiles, p => p.Name, requested, StringComparison.OrdinalIgnoreCase);
        return match?.Name is null
            ? (null, $"Browser profile {Describe(reference)} was not found in '{browser.Id}'.")
            : (match.Name, null);
    }

    private static BrowserProfileInfo? FindBy(IReadOnlyList<BrowserProfileInfo> profiles, Func<BrowserProfileInfo, string?> key, string? value, StringComparison comparison)
        => string.IsNullOrEmpty(value) ? null : profiles.FirstOrDefault(p => string.Equals(key(p), value, comparison));

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
