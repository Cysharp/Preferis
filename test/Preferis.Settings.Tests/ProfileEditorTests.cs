using Preferis.Core.Configuration;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Tests;

public sealed class ProfileEditorTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private ProfileViewModel Profile(string id, out SettingsViewModel settings)
    {
        settings = _harness.WithSample().Create();
        return settings.Profiles.Profiles.Single(p => p.Id == id);
    }

    [Fact]
    public void Browser_choices_list_detected_browsers_first_then_the_rest_of_the_catalog()
    {
        var profile = Profile("edge", out _);

        var ids = profile.BrowserChoices.Select(c => c.Id).ToList();
        Assert.Equal(["chrome", "edge", "safari", "firefox"], ids.Take(4));
        Assert.Contains("brave", ids);
        Assert.True(profile.BrowserChoices[0].IsInstalled);
        Assert.False(profile.BrowserChoices.Single(c => c.Id == "brave").IsInstalled);
        Assert.Equal("Brave (not installed)", profile.BrowserChoices.Single(c => c.Id == "brave").Title);
    }

    [Fact]
    public void The_stored_browser_and_browser_profile_are_selected()
    {
        var profile = Profile("chrome-work", out _);

        Assert.Equal(ProfileTargetKind.Browser, profile.TargetKind);
        Assert.Equal("chrome", profile.Browser?.Id);
        Assert.False(profile.PerOs);
        Assert.Equal(["(None)", "Mayuki", "Work · me@work.example.com"], profile.BrowserProfileChoices.Select(c => c.Title));
        Assert.Same(profile.BrowserProfileChoices[2], profile.BrowserProfile);
    }

    [Fact]
    public void A_stored_browser_profile_that_is_not_found_here_stays_selected()
    {
        var profile = Profile("chrome-default", out _);
        // The sample's "Chrome (Default)" is { directory: Default, name: Mayuki }: found by name.
        Assert.Equal("Mayuki", profile.BrowserProfile?.Title);

        var settings = _harness.Create();
        settings.Profiles.AddCommandProfileCommand.Execute(null);
        var other = settings.Profiles.SelectedProfile!;
        other.TargetKind = ProfileTargetKind.Browser;
        other.Browser = other.BrowserChoices.Single(c => c.Id == "chrome");
        other.Model.BrowserProfile = new BrowserProfileRef { Directory = "Profile 9", Name = "Elsewhere" };
        other.Reload();

        Assert.Equal("Elsewhere (not found on this computer)", other.BrowserProfile?.Title);
    }

    [Fact]
    public void Unrelated_edits_leave_the_stored_browser_profile_reference_alone()
    {
        // Stored with the account only (written by hand or on another machine): this machine's directory must not creep in.
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace(
            "\"browserProfile\": { \"directory\": \"Profile 1\", \"name\": \"Work\", \"email\": \"me@work.example.com\" }",
            "\"browserProfile\": { \"email\": \"me@work.example.com\", \"label\": \"from the future\" }",
            StringComparison.Ordinal));
        var settings = _harness.Create();
        var profile = settings.Profiles.Profiles.Single(p => p.Id == "chrome-work");
        Assert.Equal("Work · me@work.example.com", profile.BrowserProfile?.Title);

        profile.Private = true;

        var reference = settings.Config.Profiles.Single(p => p.Id == "chrome-work").BrowserProfile;
        Assert.Null(reference?.Directory);
        Assert.Equal("me@work.example.com", reference?.Email);
        Assert.True(reference?.ExtensionData?.ContainsKey("label"));
    }

    [Fact]
    public void Choosing_a_browser_profile_stores_its_identifiers()
    {
        var profile = Profile("chrome-default", out var settings);

        profile.BrowserProfile = profile.BrowserProfileChoices.Single(c => c.Title == "Work · me@work.example.com");

        var reference = settings.Config.Profiles.Single(p => p.Id == "chrome-default").BrowserProfile;
        Assert.Equal("Profile 1", reference?.Directory);
        Assert.Equal("me@work.example.com", reference?.Email);

        profile.BrowserProfile = profile.BrowserProfileChoices[0];
        Assert.Null(settings.Config.Profiles.Single(p => p.Id == "chrome-default").BrowserProfile);
    }

    [Fact]
    public void Changing_the_browser_drops_the_browser_profile_and_lists_the_new_ones()
    {
        var profile = Profile("chrome-work", out _);

        profile.Browser = profile.BrowserChoices.Single(c => c.Id == "edge");

        Assert.Equal("edge", profile.Model.Browser?.Default);
        Assert.Null(profile.Model.BrowserProfile);
        Assert.Equal(["(None)", "Personal · me@example.com"], profile.BrowserProfileChoices.Select(c => c.Title));
    }

    [Fact]
    public void Per_os_browsers_are_edited_separately()
    {
        var profile = Profile("system-browser", out _);
        Assert.True(profile.PerOs);
        Assert.Equal("safari", profile.MacOSBrowser?.Id);
        Assert.Equal("edge", profile.WindowsBrowser?.Id);

        profile.PerOs = false;
        Assert.Equal("safari", profile.Model.Browser?.Default);
        Assert.True(profile.Model.Browser?.IsPlain);

        profile.PerOs = true;
        profile.WindowsBrowser = profile.WindowsBrowserChoices.Single(c => c.Id == "chrome");
        Assert.Equal("safari", profile.Model.Browser?.MacOS);
        Assert.Equal("chrome", profile.Model.Browser?.Windows);
        Assert.Null(profile.Model.Browser?.Default);
    }

    [Fact]
    public void Extra_arguments_are_one_per_line()
    {
        var profile = Profile("edge", out _);

        profile.ExtraArgs = "--new-window\n\n  --lang=ja  \r\n";

        Assert.Equal(["--new-window", "--lang=ja"], profile.Model.ExtraArgs);
        profile.ExtraArgs = "";
        Assert.Null(profile.Model.ExtraArgs);
    }

    [Fact]
    public void Switching_to_a_command_and_back_keeps_what_was_entered()
    {
        var profile = Profile("edge", out _);

        profile.TargetKind = ProfileTargetKind.Command;
        Assert.Null(profile.Model.Browser);
        profile.MacOSCommandPath = "/usr/bin/open";
        profile.MacOSCommandArgs = "-a\nFirefox\n{url}";
        Assert.Equal("/usr/bin/open", profile.Model.Command?.MacOS?.Path);
        Assert.Equal(["-a", "Firefox", "{url}"], profile.Model.Command?.MacOS?.Args);
        Assert.Null(profile.Model.Command?.Windows);

        profile.TargetKind = ProfileTargetKind.Browser;
        Assert.Null(profile.Model.Command);
        Assert.Equal("edge", profile.Model.Browser?.Default);
    }

    [Fact]
    public async Task Browsing_for_an_app_bundle_opens_it_through_open()
    {
        var profile = Profile("custom-tool", out _);
        _harness.Dialogs.Files.Enqueue("/Applications/Firefox.app");

        await profile.BrowseMacOSCommandCommand.ExecuteAsync(null);

        Assert.Equal("/usr/bin/open", profile.Model.Command?.MacOS?.Path);
        Assert.Equal(["-a", "/Applications/Firefox.app", "{url}"], profile.Model.Command?.MacOS?.Args);
    }

    [Fact]
    public async Task Browsing_for_an_executable_keeps_the_arguments()
    {
        var profile = Profile("custom-tool", out _);
        _harness.Dialogs.Files.Enqueue("/usr/local/bin/foo");

        await profile.BrowseMacOSCommandCommand.ExecuteAsync(null);

        Assert.Equal("/usr/local/bin/foo", profile.Model.Command?.MacOS?.Path);
        Assert.Equal(["{url}"], profile.Model.Command?.MacOS?.Args);
    }

    [Fact]
    public void Unknown_properties_of_a_command_survive_editing_it()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace(
            "\"path\": \"/Applications/Foo.app/Contents/MacOS/Foo\",",
            "\"path\": \"/Applications/Foo.app/Contents/MacOS/Foo\", \"workingDirectory\": \"~\",",
            StringComparison.Ordinal));
        var settings = _harness.Create();
        var profile = settings.Profiles.Profiles.Single(p => p.Id == "custom-tool");

        profile.MacOSCommandPath = "/Applications/Bar.app/Contents/MacOS/Bar";

        Assert.Contains("workingDirectory", ConfigSerializer.Serialize(settings.Config), StringComparison.Ordinal);
    }

    [Fact]
    public void Fallback_choices_are_the_other_profiles()
    {
        var profile = Profile("chrome-work", out var settings);

        Assert.DoesNotContain(profile.FallbackChoices, c => c.Id == "chrome-work");
        Assert.Equal("edge", profile.Fallback?.Id);

        profile.Fallback = profile.FallbackChoices[0];
        Assert.Null(settings.Config.Profiles.Single(p => p.Id == "chrome-work").Fallback);
    }

    [Fact]
    public void Private_mode_writes_through()
    {
        var profile = Profile("edge", out var settings);

        profile.Private = true;

        Assert.True(settings.Config.Profiles[0].Private);
        Assert.True(settings.IsDirty);
    }

    [Fact]
    public void Issues_are_shown_on_the_profile_they_belong_to()
    {
        var profile = Profile("custom-tool", out var settings);

        profile.MacOSCommandPath = "";
        profile.WindowsCommandPath = "";

        Assert.Single(profile.Issues);
        Assert.Empty(settings.Profiles.Profiles[0].Issues);
    }
}
