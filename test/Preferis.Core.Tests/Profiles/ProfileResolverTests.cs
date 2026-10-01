using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Profiles;

public class ProfileResolverTests
{
    private static readonly RoutingUrl Url = RoutingUrl.Parse("https://example.com/path?q=1");

    private sealed class FakeLocator : IBrowserLocator
    {
        public Dictionary<string, DetectedBrowser> Browsers { get; } = new(StringComparer.Ordinal);

        public DetectedBrowser? TryResolve(string browserId) => Browsers.GetValueOrDefault(browserId);
    }

    private sealed class FakeProfiles : IBrowserProfileEnumerator
    {
        public Dictionary<string, IReadOnlyList<BrowserProfileInfo>?> ByBrowserId { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<BrowserProfileInfo>? Enumerate(DetectedBrowser browser) => ByBrowserId.GetValueOrDefault(browser.Id);
    }

    private static DetectedBrowser Detected(string id, string path, string? appPath = null)
    {
        var entry = BrowserCatalog.Find(id);
        return new DetectedBrowser(id, entry?.DisplayName ?? id, entry?.Family ?? BrowserFamily.Other, path, appPath, null, entry);
    }

    private static (ProfileResolver Resolver, FakeLocator Locator, FakeProfiles Profiles) Setup(HostPlatform platform)
    {
        var locator = new FakeLocator();
        var profiles = new FakeProfiles();
        locator.Browsers["edge"] = Detected("edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe");
        locator.Browsers["chrome"] = Detected("chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/Applications/Google Chrome.app");
        locator.Browsers["firefox"] = Detected("firefox", "/Applications/Firefox.app/Contents/MacOS/firefox", "/Applications/Firefox.app");
        locator.Browsers["safari"] = Detected("safari", "/Applications/Safari.app/Contents/MacOS/Safari", "/Applications/Safari.app");
        profiles.ByBrowserId["chrome"] =
        [
            new BrowserProfileInfo("Default", "Mayuki", null),
            new BrowserProfileInfo("Profile 7", "Work", "me@work.example.com"),
        ];
        profiles.ByBrowserId["firefox"] = [new BrowserProfileInfo("abcd.work", "Work", null)];
        var resolver = new ProfileResolver(platform, locator, profiles, _ => null, "/Users/me");
        return (resolver, locator, profiles);
    }

    private static Profile Chrome(BrowserProfileRef? browserProfile = null, bool isPrivate = false, List<string>? extraArgs = null)
        => new() { Id = "p", Name = "p", Browser = new BrowserReference("chrome"), BrowserProfile = browserProfile, Private = isPrivate, ExtraArgs = extraArgs };

    private static LaunchPlan Plan(ResolveResult result) => Assert.IsType<ResolveResult.Resolved>(result).Plan;

    [Fact]
    public void Chromium_profile_is_matched_by_email_before_anything_else()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var profile = Chrome(new BrowserProfileRef { Directory = "Profile 1", Name = "Old Name", Email = "ME@work.example.com" });

        var plan = Plan(resolver.Resolve(profile, Url));

        Assert.Equal(LaunchKind.Execute, plan.Kind);
        Assert.Equal("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", plan.Path);
        Assert.Equal(["--profile-directory=Profile 7", "https://example.com/path?q=1"], plan.Arguments);
        Assert.Equal("Profile 7", plan.BrowserProfileDirectory);
    }

    [Fact]
    public void Chromium_profile_falls_back_to_name_then_directory()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);

        var byName = Plan(resolver.Resolve(Chrome(new BrowserProfileRef { Directory = "Profile 1", Name = "work" }), Url));
        var byDirectory = Plan(resolver.Resolve(Chrome(new BrowserProfileRef { Directory = "Default", Name = "Someone Else" }), Url));

        Assert.Equal("Profile 7", byName.BrowserProfileDirectory);
        Assert.Equal("Default", byDirectory.BrowserProfileDirectory);
    }

    [Fact]
    public void Unreadable_profile_data_falls_back_to_the_stored_directory_with_a_warning()
    {
        var (resolver, _, profiles) = Setup(HostPlatform.MacOS);
        profiles.ByBrowserId["chrome"] = null;

        var result = Assert.IsType<ResolveResult.Resolved>(resolver.Resolve(Chrome(new BrowserProfileRef { Directory = "Profile 1", Email = "me@work.example.com" }), Url));

        Assert.Equal("Profile 1", result.Plan.BrowserProfileDirectory);
        Assert.Contains(result.Warnings, w => w.Contains("Profile 1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_profile_that_matches_nothing_is_unresolved()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);

        var result = resolver.Resolve(Chrome(new BrowserProfileRef { Directory = "Profile 9", Name = "Nobody", Email = "nobody@example.com" }), Url);

        var unresolved = Assert.IsType<ResolveResult.Unresolved>(result);
        Assert.Contains("profile", unresolved.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Private_mode_uses_the_browser_specific_flag()
    {
        var (resolver, _, _) = Setup(HostPlatform.Windows);
        var edge = new Profile { Id = "e", Name = "e", Browser = new BrowserReference("edge"), Private = true };

        Assert.Equal(["--inprivate", "https://example.com/path?q=1"], Plan(resolver.Resolve(edge, Url)).Arguments);
        Assert.Equal(["--incognito", "https://example.com/path?q=1"], Plan(resolver.Resolve(Chrome(isPrivate: true), Url)).Arguments);
    }

    [Fact]
    public void Extra_arguments_come_after_flags_and_before_the_url()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var profile = Chrome(new BrowserProfileRef { Directory = "Default" }, isPrivate: true, extraArgs: ["--new-window"]);

        Assert.Equal(["--profile-directory=Default", "--incognito", "--new-window", "https://example.com/path?q=1"], Plan(resolver.Resolve(profile, Url)).Arguments);
    }

    [Fact]
    public void Firefox_profiles_are_selected_by_name()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var firefox = new Profile { Id = "f", Name = "f", Browser = new BrowserReference("firefox"), BrowserProfile = new BrowserProfileRef { Name = "Work" }, Private = true };

        var plan = Plan(resolver.Resolve(firefox, Url));

        Assert.Equal(["-P", "Work", "-private-window", "https://example.com/path?q=1"], plan.Arguments);
    }

    [Fact]
    public void Browsers_without_cli_support_are_opened_through_the_application_on_macos()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var safari = new Profile { Id = "s", Name = "s", Browser = new BrowserReference("safari"), BrowserProfile = new BrowserProfileRef { Name = "Personal" } };

        var result = Assert.IsType<ResolveResult.Resolved>(resolver.Resolve(safari, Url));

        Assert.Equal(LaunchKind.OpenWithApplication, result.Plan.Kind);
        Assert.Equal("/Applications/Safari.app", result.Plan.Path);
        Assert.Equal(["https://example.com/path?q=1"], result.Plan.Arguments);
        Assert.Contains(result.Warnings, w => w.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_browser_that_is_not_installed_is_unresolved()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var brave = new Profile { Id = "b", Name = "b", Browser = new BrowserReference("brave") };

        var unresolved = Assert.IsType<ResolveResult.Unresolved>(resolver.Resolve(brave, Url));
        Assert.Contains("brave", unresolved.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Per_os_browser_references_pick_the_current_platform()
    {
        var profile = new Profile { Id = "sys", Name = "sys", Browser = new BrowserReference { Windows = "edge", MacOS = "safari" } };

        Assert.Equal("/Applications/Safari.app", Plan(Setup(HostPlatform.MacOS).Resolver.Resolve(profile, Url)).Path);
        Assert.EndsWith("msedge.exe", Plan(Setup(HostPlatform.Windows).Resolver.Resolve(profile, Url)).Path);
    }

    [Fact]
    public void Platform_specific_ids_are_passed_to_the_locator_as_is()
    {
        var (resolver, locator, _) = Setup(HostPlatform.MacOS);
        locator.Browsers["mac:com.example.Browser"] = new DetectedBrowser("mac:com.example.Browser", "Example", BrowserFamily.Other, "/Applications/Example.app/Contents/MacOS/Example", "/Applications/Example.app", null, null);
        var profile = new Profile { Id = "x", Name = "x", Browser = new BrowserReference("mac:com.example.Browser") };

        var plan = Plan(resolver.Resolve(profile, Url));

        Assert.Equal(LaunchKind.OpenWithApplication, plan.Kind);
        Assert.Equal("/Applications/Example.app", plan.Path);
    }

    [Fact]
    public void Commands_expand_environment_variables_and_the_url_placeholder()
    {
        var locator = new FakeLocator();
        var resolver = new ProfileResolver(HostPlatform.Windows, locator, new FakeProfiles(), name => name == "LOCALAPPDATA" ? @"C:\Users\me\AppData\Local" : null, @"C:\Users\me");
        var profile = new Profile
        {
            Id = "tool",
            Name = "tool",
            Command = new CommandTarget { Windows = new CommandSpec { Path = @"%LOCALAPPDATA%\Programs\Foo\foo.exe", Args = ["--open", "{url}"] } },
        };

        var plan = Plan(resolver.Resolve(profile, Url));

        Assert.Equal(LaunchKind.Execute, plan.Kind);
        Assert.Equal(@"C:\Users\me\AppData\Local\Programs\Foo\foo.exe", plan.Path);
        Assert.Equal(["--open", "https://example.com/path?q=1"], plan.Arguments);
    }

    [Fact]
    public void Commands_without_a_placeholder_get_the_url_appended()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var profile = new Profile { Id = "tool", Name = "tool", Command = new CommandTarget { MacOS = new CommandSpec { Path = "~/bin/tool", Args = ["--verbose"] } } };

        var plan = Plan(resolver.Resolve(profile, Url));

        Assert.Equal("/Users/me/bin/tool", plan.Path);
        Assert.Equal(["--verbose", "https://example.com/path?q=1"], plan.Arguments);
    }

    [Fact]
    public void A_command_for_another_platform_only_is_unresolved()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var profile = new Profile { Id = "tool", Name = "tool", Command = new CommandTarget { Windows = new CommandSpec { Path = "foo.exe" } } };

        Assert.IsType<ResolveResult.Unresolved>(resolver.Resolve(profile, Url));
    }

    [Fact]
    public void Fallback_profiles_are_followed_until_one_resolves()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var config = PreferisConfig.CreateDefault();
        config.Profiles.Add(new Profile { Id = "brave", Name = "Brave", Browser = new BrowserReference("brave"), Fallback = "edge-win" });
        config.Profiles.Add(new Profile { Id = "edge-win", Name = "Edge", Browser = new BrowserReference { Windows = "edge" }, Fallback = "safari" });
        config.Profiles.Add(new Profile { Id = "safari", Name = "Safari", Browser = new BrowserReference("safari") });

        var result = Assert.IsType<ResolveResult.Resolved>(resolver.ResolveWithFallback(config, "brave", Url));

        Assert.Equal("safari", result.ProfileId);
        Assert.Equal("/Applications/Safari.app", result.Plan.Path);
        Assert.Equal(2, result.Warnings.Count(w => w.Contains("fallback", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Fallback_cycles_and_unknown_profiles_are_unresolved()
    {
        var (resolver, _, _) = Setup(HostPlatform.MacOS);
        var config = PreferisConfig.CreateDefault();
        config.Profiles.Add(new Profile { Id = "a", Name = "a", Browser = new BrowserReference("brave"), Fallback = "b" });
        config.Profiles.Add(new Profile { Id = "b", Name = "b", Browser = new BrowserReference("vivaldi"), Fallback = "a" });

        Assert.IsType<ResolveResult.Unresolved>(resolver.ResolveWithFallback(config, "a", Url));
        Assert.IsType<ResolveResult.Unresolved>(resolver.ResolveWithFallback(config, "missing", Url));
    }
}
