using Preferis.Core.Configuration;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

public sealed class ProfilesPageTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static DetectedItemViewModel Item(ProfilesPageViewModel page, string browserId, string? directory = null)
        => page.DetectedBrowsers.Single(b => b.Id == browserId).Items.Single(i => i.Profile?.Directory == directory);

    [Fact]
    public void Detected_browsers_are_listed_with_their_profiles()
    {
        var page = _harness.Create().Profiles;

        Assert.Equal(["chrome", "edge", "safari", "firefox"], page.DetectedBrowsers.Select(b => b.Id));
        var chrome = page.DetectedBrowsers[0];
        Assert.Equal("Google Chrome", chrome.Name);
        Assert.Equal([null, "Default", "Profile 1"], chrome.Items.Select(i => i.Profile?.Directory));
        Assert.Equal("Work", chrome.Items[2].Title);
        Assert.Equal("me@work.example.com", chrome.Items[2].Subtitle);
        Assert.Single(page.DetectedBrowsers[2].Items);
    }

    [Fact]
    public void Adding_a_detected_browser_creates_and_selects_a_profile()
    {
        var settings = _harness.Create();
        var page = settings.Profiles;

        page.AddDetectedCommand.Execute(Item(page, "safari"));

        var profile = Assert.Single(page.Profiles);
        Assert.Same(profile, page.SelectedProfile);
        Assert.Equal("profile-1", profile.Id);
        Assert.Equal("Safari", profile.Name);
        Assert.Equal("safari", profile.Model.Browser?.Default);
        Assert.Null(profile.Model.BrowserProfile);
        Assert.True(settings.IsDirty);
    }

    [Fact]
    public void Adding_a_chromium_browser_profile_stores_every_identifier()
    {
        var page = _harness.Create().Profiles;

        page.AddDetectedCommand.Execute(Item(page, "chrome", "Profile 1"));

        var profile = Assert.Single(page.Profiles).Model;
        Assert.Equal("profile-1", profile.Id);
        Assert.Equal("Google Chrome (Work)", profile.Name);
        Assert.Equal("chrome", profile.Browser?.Default);
        Assert.Equal("Profile 1", profile.BrowserProfile?.Directory);
        Assert.Equal("Work", profile.BrowserProfile?.Name);
        Assert.Equal("me@work.example.com", profile.BrowserProfile?.Email);
    }

    [Fact]
    public void Adding_a_firefox_profile_stores_its_name()
    {
        var page = _harness.Create().Profiles;

        page.AddDetectedCommand.Execute(Item(page, "firefox", "Profiles/def.work"));

        var reference = Assert.Single(page.Profiles).Model.BrowserProfile;
        Assert.Equal("Work", reference?.Name);
        Assert.Null(reference?.Directory);
    }

    [Fact]
    public void The_first_profile_becomes_the_default()
    {
        var settings = _harness.Create();
        var page = settings.Profiles;

        page.AddDetectedCommand.Execute(Item(page, "safari"));
        page.AddDetectedCommand.Execute(Item(page, "edge"));

        Assert.Equal("profile-1", settings.Config.DefaultProfile);
        Assert.True(page.Profiles[0].IsDefault);
        Assert.False(page.Profiles[1].IsDefault);
    }

    [Fact]
    public void Adding_the_same_browser_twice_gets_a_new_id_and_name()
    {
        var settings = _harness.Create();
        var page = settings.Profiles;

        page.AddDetectedCommand.Execute(Item(page, "safari"));
        page.AddDetectedCommand.Execute(Item(page, "safari"));

        Assert.Equal(["profile-1", "profile-2"], page.Profiles.Select(p => p.Id));
        Assert.Equal(["Safari", "Safari 2"], page.Profiles.Select(p => p.Name));
        Assert.Empty(settings.Issues);
    }

    [Fact]
    public void Add_all_creates_one_profile_per_browser_profile_not_yet_configured()
    {
        var page = _harness.Create().Profiles;
        page.AddDetectedCommand.Execute(Item(page, "chrome", "Default"));

        page.AddAllDetectedCommand.Execute(null);

        Assert.Equal(["profile-1", "profile-2", "profile-3", "profile-4", "profile-5", "profile-6"], page.Profiles.Select(p => p.Id));
        Assert.Equal(
            ["Google Chrome (Mayuki)", "Google Chrome (Work)", "Microsoft Edge (Personal)", "Safari", "Mozilla Firefox (default-release)", "Mozilla Firefox (Work)"],
            page.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void A_command_profile_can_be_added()
    {
        var page = _harness.Create().Profiles;

        page.AddCommandProfileCommand.Execute(null);
        page.AddCommandProfileCommand.Execute(null);

        var profile = page.Profiles[0];
        Assert.Equal(ProfileTargetKind.Command, profile.TargetKind);
        Assert.NotNull(profile.Model.Command);
        Assert.Null(profile.Model.Browser);
        Assert.Equal(["Command", "Command 2"], page.Profiles.Select(p => p.Name));
    }

    [Fact]
    public void A_renamed_profile_keeps_its_id_and_a_name_in_use_is_an_error()
    {
        var settings = _harness.WithSample().Create();
        var profile = settings.Profiles.Profiles.Single(p => p.Id == "chrome-work");

        profile.Name = "microsoft edge";

        Assert.Equal("chrome-work", profile.Id);
        Assert.Contains(profile.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("“Microsoft Edge”", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicating_copies_the_profile_under_a_new_id()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;
        page.SelectedProfile = page.Profiles[2];

        page.DuplicateCommand.Execute(null);

        var copy = page.SelectedProfile!;
        Assert.Equal(3, page.Profiles.IndexOf(copy));
        Assert.Equal("profile-1", copy.Id);
        Assert.Equal("Chrome (Work) copy", copy.Name);
        Assert.Equal("Profile 1", copy.Model.BrowserProfile?.Directory);
        Assert.NotSame(page.Profiles[2].Model.BrowserProfile, copy.Model.BrowserProfile);

        page.SelectedProfile = page.Profiles[2];
        page.DuplicateCommand.Execute(null);
        Assert.Equal("profile-2", page.SelectedProfile!.Id);
        Assert.Equal("Chrome (Work) copy 2", page.SelectedProfile.Name);
    }

    [Fact]
    public async Task Deleting_a_profile_in_use_asks_and_removes_the_references()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;
        page.SelectedProfile = page.Profiles.Single(p => p.Id == "edge");

        await page.DeleteCommand.ExecuteAsync(null);
        Assert.Equal(6, page.Profiles.Count);

        _harness.Dialogs.Answers.Enqueue("Delete");
        await page.DeleteCommand.ExecuteAsync(null);

        var config = settings.Config;
        Assert.DoesNotContain(config.Profiles, p => p.Id == "edge");
        Assert.Null(config.DefaultProfile);
        Assert.Null(config.Profiles.Single(p => p.Id == "chrome-work").Fallback);
        var msLogin = config.Rules.Single(r => r.Id == "ms-login");
        Assert.Equal(["chrome-work", "chrome-default"], Assert.IsType<OpenAction>(msLogin.Actions[0]).Profiles);
        Assert.Contains("ms-login", _harness.Dialogs.Asked[^1].Message, StringComparison.Ordinal);
        Assert.Empty(settings.Issues);
    }

    [Fact]
    public async Task Deleting_an_unused_profile_does_not_ask()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;
        page.SelectedProfile = page.Profiles.Single(p => p.Id == "custom-tool");

        await page.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(_harness.Dialogs.Asked);
        Assert.Equal(5, page.Profiles.Count);
        Assert.Equal(page.Profiles[4], page.SelectedProfile);
    }

    [Fact]
    public void Profiles_can_be_reordered()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;
        page.SelectedProfile = page.Profiles[1];

        page.MoveUpCommand.Execute(null);

        Assert.Equal("chrome-default", settings.Config.Profiles[0].Id);
        Assert.Equal("chrome-default", page.Profiles[0].Id);
        Assert.False(page.MoveUpCommand.CanExecute(null));
        Assert.True(page.MoveDownCommand.CanExecute(null));
    }

    [Fact]
    public void A_profile_can_be_moved_to_any_position()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;

        page.MoveProfile(page.Profiles[5], 1);

        Assert.Equal(["edge", "custom-tool", "chrome-default", "chrome-work", "edge-private", "system-browser"], settings.Config.Profiles.Select(p => p.Id));
        Assert.Equal(settings.Config.Profiles.Select(p => p.Id), page.Profiles.Select(p => p.Id));
    }

    [Fact]
    public void Set_default_marks_the_selected_profile()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;
        page.SelectedProfile = page.Profiles[2];

        page.SetDefaultCommand.Execute(null);

        Assert.Equal("chrome-work", settings.Config.DefaultProfile);
        Assert.Equal([false, false, true, false, false, false], page.Profiles.Select(p => p.IsDefault));
    }
}
