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
    private readonly ProfileTargetResolver _targets;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _homeDirectory;

    public ProfileResolver(HostPlatform platform, IBrowserLocator locator, IBrowserProfileEnumerator profiles, Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        _platform = platform;
        _targets = new ProfileTargetResolver(platform, locator, profiles);
        _getEnvironmentVariable = getEnvironmentVariable;
        _homeDirectory = homeDirectory;
    }

    /// <summary>Resolves a profile by id, following <see cref="Profile.Fallback"/> links until one resolves.</summary>
    public ResolveResult ResolveWithFallback(PreferisConfig config, string profileId, RoutingUrl url)
        => CreatePlan(_targets.ResolveWithFallback(config, profileId), url);

    public ResolveResult Resolve(Profile profile, RoutingUrl url)
        => CreatePlan(_targets.Resolve(profile), url);

    private ResolveResult CreatePlan(ProfileTargetResolution resolution, RoutingUrl url)
    {
        if (resolution is ProfileTargetResolution.Unresolved unresolved)
        {
            return new ResolveResult.Unresolved(unresolved.Reason);
        }
        var resolved = (ProfileTargetResolution.Resolved)resolution;
        if (resolved.Target is ResolvedProfileTarget.Command command)
        {
            return ResolveCommand(resolved.Profile, command.Spec, resolved.Warnings, url);
        }

        var profile = resolved.Profile;
        var target = (ResolvedProfileTarget.Browser)resolved.Target;
        var browser = target.Installation;
        var profileDirectory = target.ProfileDirectory;
        var warnings = resolved.Warnings.ToList();
        var arguments = new List<string>();

        switch (browser.Family)
        {
            case BrowserFamily.Chromium:
                if (profileDirectory is not null)
                {
                    arguments.Add($"--profile-directory={profileDirectory}");
                }
                break;

            case BrowserFamily.Firefox:
                if (profileDirectory is not null)
                {
                    arguments.Add("-P");
                    arguments.Add(profileDirectory);
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

    private ResolveResult ResolveCommand(Profile profile, CommandSpec spec, IReadOnlyList<string> warnings, RoutingUrl url)
    {
        var path = EnvironmentExpander.Expand(spec.Path, _platform, _getEnvironmentVariable, _homeDirectory);
        var arguments = spec.Args.Select(a => a.Replace(UrlPlaceholder, url.AbsoluteUri, StringComparison.Ordinal)).ToList();
        if (!spec.Args.Any(a => a.Contains(UrlPlaceholder, StringComparison.Ordinal)))
        {
            arguments.Add(url.AbsoluteUri);
        }

        return new ResolveResult.Resolved(new LaunchPlan(LaunchKind.Execute, path, arguments, null, null), warnings, profile.Id);
    }

    private static string? DefaultPrivateFlag(BrowserFamily family) => family switch
    {
        BrowserFamily.Chromium => "--incognito",
        BrowserFamily.Firefox => "-private-window",
        _ => null,
    };
}
