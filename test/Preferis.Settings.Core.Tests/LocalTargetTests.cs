using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

/// <summary>This machine's own target for a profile (local.json profileTargets) and changing profile ids.</summary>
public sealed class LocalTargetTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private ProfileViewModel Profile(SettingsViewModel settings, string id) => settings.Profiles.Profiles.Single(p => p.Id == id);

    private void WriteLocal(string json) => File.WriteAllText(_harness.Paths.LocalConfigPath, json);

    [Fact]
    public void Using_a_target_of_this_computer_starts_from_what_the_shared_one_opens_here()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "chrome-work");

        profile.UsesLocalTarget = true;

        var target = settings.Local.ProfileTargets?["chrome-work"];
        Assert.Equal("chrome", target?.Browser);
        Assert.Equal("me@work.example.com", target?.BrowserProfile?.Email);
        Assert.NotNull(profile.LocalTarget);
        Assert.Equal("chrome", profile.LocalTarget.Browser?.Id);
        Assert.Equal("Work · me@work.example.com", profile.LocalTarget.BrowserProfile?.Title);
    }

    [Fact]
    public async Task Editing_this_computers_target_changes_local_json_only()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "chrome-work");
        profile.UsesLocalTarget = true;

        profile.LocalTarget!.Browser = profile.LocalTarget.BrowserChoices.Single(c => c.Id == "safari");
        profile.LocalTarget.Private = true;
        await _harness.Saves.RunPendingAsync();

        var saved = _harness.Loader.LoadLocal().ProfileTargets?["chrome-work"];
        Assert.Equal("safari", saved?.Browser);
        Assert.Null(saved?.BrowserProfile);
        Assert.True(saved?.Private);
        Assert.Equal("chrome", _harness.SavedConfig().Profiles.Single(p => p.Id == "chrome-work").Browser?.Default);
        Assert.Equal("This computer: Safari · Private", profile.Summary);
    }

    [Fact]
    public void The_list_shows_the_browser_of_this_computer()
    {
        _harness.Platform.Browsers[0] = _harness.Platform.Browsers[0] with { IconPng = [1] };
        _harness.Platform.Browsers[2] = _harness.Platform.Browsers[2] with { IconPng = [3] };
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "chrome-work");
        Assert.Equal([1], profile.IconPng);

        profile.UsesLocalTarget = true;
        profile.LocalTarget!.Browser = profile.LocalTarget.BrowserChoices.Single(c => c.Id == "safari");

        Assert.Equal([3], profile.IconPng);
    }

    [Fact]
    public void This_computers_target_can_be_a_command()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "edge");
        profile.UsesLocalTarget = true;
        _harness.Dialogs.Files.Enqueue("/Applications/Firefox.app");

        profile.LocalTarget!.TargetKind = ProfileTargetKind.Command;
        profile.LocalTarget.BrowseCommandCommand.Execute(null);

        var target = settings.Local.ProfileTargets!["edge"];
        Assert.Null(target.Browser);
        Assert.Equal("/usr/bin/open", target.Command?.Path);
        Assert.Equal(["-a", "/Applications/Firefox.app", "{url}"], target.Command?.Args);
        Assert.Equal("This computer: Command: open", profile.Summary);
    }

    [Fact]
    public void Turning_it_off_goes_back_to_the_shared_target()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "edge");
        profile.UsesLocalTarget = true;

        profile.UsesLocalTarget = false;

        Assert.False(settings.Local.ProfileTargets?.ContainsKey("edge") ?? false);
        Assert.Null(profile.LocalTarget);
        Assert.Equal("Microsoft Edge", profile.Summary);
    }

    [Fact]
    public async Task An_empty_profile_is_set_on_each_computer()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;

        page.AddEmptyProfileCommand.Execute(null);
        await _harness.Saves.RunPendingAsync();

        var profile = page.SelectedProfile!;
        Assert.Equal("New Profile", profile.Name);
        Assert.Equal(ProfileTargetKind.EachComputer, profile.TargetKind);
        var saved = _harness.SavedConfig().Profiles.Single(p => p.Id == profile.Id);
        Assert.Null(saved.Browser);
        Assert.Null(saved.Command);
        // Its editor is there to choose from, but nothing is written to local.json until something is chosen.
        Assert.NotNull(profile.LocalTarget);
        Assert.False(_harness.Loader.LoadLocal().ProfileTargets?.ContainsKey(profile.Id) ?? false);
        Assert.Equal("Not set on this computer", profile.Summary);
        var issue = Assert.Single(profile.Issues);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("Not set on this computer; the default profile or the picker is used instead.", issue.Message);
    }

    [Fact]
    public async Task What_a_profile_set_on_each_computer_opens_here_is_saved_in_local_json()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.AddEmptyProfileCommand.Execute(null);
        var profile = settings.Profiles.SelectedProfile!;

        profile.LocalTarget!.Browser = profile.LocalTarget.BrowserChoices.Single(c => c.Id == "safari");
        await _harness.Saves.RunPendingAsync();

        Assert.Equal("safari", _harness.Loader.LoadLocal().ProfileTargets?[profile.Id].Browser);
        Assert.Null(_harness.SavedConfig().Profiles.Single(p => p.Id == profile.Id).Browser);
        Assert.Equal("This computer: Safari", profile.Summary);
        Assert.Empty(profile.Issues);

        var reopened = Profile(_harness.Create(), profile.Id);
        Assert.Equal(ProfileTargetKind.EachComputer, reopened.TargetKind);
        Assert.Equal("safari", reopened.LocalTarget?.Browser?.Id);
    }

    [Fact]
    public async Task Setting_a_profile_on_each_computer_keeps_what_this_computer_opens()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "chrome-work");

        profile.TargetKind = ProfileTargetKind.EachComputer;
        await _harness.Saves.RunPendingAsync();

        var saved = _harness.SavedConfig().Profiles.Single(p => p.Id == "chrome-work");
        Assert.Null(saved.Browser);
        Assert.Null(saved.BrowserProfile);
        var target = _harness.Loader.LoadLocal().ProfileTargets?["chrome-work"];
        Assert.Equal("chrome", target?.Browser);
        Assert.Equal("me@work.example.com", target?.BrowserProfile?.Email);
        Assert.Empty(profile.Issues);

        // Back to a shared browser: the one it had, and this computer keeps its own.
        profile.TargetKind = ProfileTargetKind.Browser;

        Assert.Equal("chrome", settings.Config.Profiles.Single(p => p.Id == "chrome-work").Browser?.Default);
        Assert.True(profile.UsesLocalTarget);
    }

    [Fact]
    public void A_shared_target_chosen_before_this_computers_leaves_no_local_target()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.AddEmptyProfileCommand.Execute(null);
        var profile = settings.Profiles.SelectedProfile!;

        profile.TargetKind = ProfileTargetKind.Browser;

        Assert.False(profile.UsesLocalTarget);
        Assert.Null(profile.LocalTarget);
        Assert.False(settings.Local.ProfileTargets?.ContainsKey(profile.Id) ?? false);
        // A browser is chosen right away; a shared target without one would be read back as set on each computer.
        Assert.Equal("chrome", profile.Browser?.Id);
        Assert.Equal("chrome", settings.Config.Profiles.Single(p => p.Id == profile.Id).Browser?.Default);
    }

    [Fact]
    public void Targets_in_local_json_are_shown_when_the_settings_open()
    {
        _harness.WithSample();
        WriteLocal("""{ "profileTargets": { "chrome-work": { "browser": "firefox", "browserProfile": { "name": "Work" } } } }""");

        var profile = Profile(_harness.Create(), "chrome-work");

        Assert.True(profile.UsesLocalTarget);
        Assert.Equal("firefox", profile.LocalTarget?.Browser?.Id);
        Assert.Equal("Work", profile.LocalTarget?.BrowserProfile?.Title);
        Assert.Equal("This computer: Mozilla Firefox · Work", profile.Summary);
    }

    [Fact]
    public void A_profile_this_computer_cannot_open_is_flagged()
    {
        _harness.Platform.Browsers.RemoveAll(b => b.Browser.Id == "chrome");
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "chrome-work");

        var issue = Assert.Single(profile.Issues);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("Google Chrome is not installed on this computer", issue.Message, StringComparison.Ordinal);
        Assert.Contains("“Microsoft Edge” is used instead", issue.Message, StringComparison.Ordinal);
        Assert.Contains(settings.Issues, i => i == issue);

        profile.UsesLocalTarget = true;
        profile.LocalTarget!.Browser = profile.LocalTarget.BrowserChoices.Single(c => c.Id == "edge");

        Assert.Empty(profile.Issues);
    }

    [Fact]
    public void A_browser_profile_missing_on_this_computer_is_flagged()
    {
        _harness.Platform.Browsers[0] = _harness.Platform.Browsers[0] with { Profiles = [new BrowserProfileInfo("Default", "Mayuki", null)] };
        var settings = _harness.WithSample().Create();

        var issue = Assert.Single(Profile(settings, "chrome-work").Issues);

        Assert.Contains("“Work” was not found in Google Chrome", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unreadable_browser_profiles_need_a_stored_directory_for_chromium(bool emptyList)
    {
        _harness.Platform.Browsers[0] = _harness.Platform.Browsers[0] with { Profiles = emptyList ? [] : null };
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """
            { "profiles": [
                { "id": "work", "name": "Work", "browser": "chrome", "browserProfile": { "name": "Work", "email": "work@example.com" } }
            ] }
            """);

        var issue = Assert.Single(Profile(_harness.Create(), "work").Issues);

        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("no profile directory is stored", issue.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Availability_skips_a_fallback_whose_browser_profile_cannot_be_resolved(bool emptyList)
    {
        _harness.Platform.Browsers[0] = _harness.Platform.Browsers[0] with { Profiles = emptyList ? [] : null };
        _harness.Platform.Browsers.RemoveAll(b => b.Browser.Id == "edge");
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """
            { "profiles": [
                { "id": "missing", "name": "Missing", "browser": "edge", "fallback": "work" },
                { "id": "work", "name": "Work", "browser": "chrome", "browserProfile": { "name": "Work" }, "fallback": "safari" },
                { "id": "safari", "name": "Safari", "browser": "safari" }
            ] }
            """);

        var issue = Assert.Single(Profile(_harness.Create(), "missing").Issues);

        Assert.Contains("“Safari” is used instead", issue.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("“Work” is used instead", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_for_the_other_os_only_is_flagged()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """{ "profiles": [ { "id": "work", "name": "Work", "browser": { "windows": "edge" } } ] }""");

        var issue = Assert.Single(_harness.Create().Issues);

        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("No browser is set for macOS; the default profile or the picker is used instead.", issue.Message);
    }

    [Fact]
    public async Task A_command_of_this_computer_without_a_program_is_flagged()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "edge");
        profile.UsesLocalTarget = true;

        profile.LocalTarget!.TargetKind = ProfileTargetKind.Command;

        Assert.Contains("has no program", Assert.Single(profile.Issues).Message, StringComparison.Ordinal);
        await _harness.Saves.RunPendingAsync();
        Assert.Equal(ProfileTargetKind.Command, Profile(_harness.Create(), "edge").LocalTarget?.TargetKind);
    }

    [Fact]
    public async Task Deleting_a_profile_forgets_its_target_and_duplicating_copies_it()
    {
        var settings = _harness.WithSample().Create();
        var page = settings.Profiles;
        var profile = Profile(settings, "edge-private");
        profile.UsesLocalTarget = true;
        profile.LocalTarget!.Browser = profile.LocalTarget.BrowserChoices.Single(c => c.Id == "safari");

        page.SelectedProfile = profile;
        page.DuplicateCommand.Execute(null);
        var copy = page.SelectedProfile!;
        Assert.Equal("safari", settings.Local.ProfileTargets![copy.Id].Browser);
        Assert.NotSame(settings.Local.ProfileTargets["edge-private"], settings.Local.ProfileTargets[copy.Id]);

        page.SelectedProfile = profile;
        _harness.Dialogs.Answers.Enqueue("Delete");
        await page.DeleteCommand.ExecuteAsync(null);
        Assert.False(settings.Local.ProfileTargets.ContainsKey("edge-private"));
    }

    [Fact]
    public async Task A_target_kept_under_a_previous_id_moves_to_the_current_one_when_the_settings_open()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace(
            "\"id\": \"chrome-work\",",
            "\"id\": \"work\", \"previousIds\": [\"chrome-work\"],",
            StringComparison.Ordinal));
        WriteLocal("""{ "profileTargets": { "chrome-work": { "browser": "safari" } } }""");

        var settings = _harness.Create();
        await _harness.Saves.RunPendingAsync();

        Assert.True(Profile(settings, "work").UsesLocalTarget);
        Assert.Equal(["work"], _harness.Loader.LoadLocal().ProfileTargets?.Keys);
    }

    [Fact]
    public void The_test_page_routes_with_the_targets_of_this_computer()
    {
        _harness.WithSample();
        WriteLocal("""{ "profileTargets": { "chrome-work": { "browser": "safari" } } }""");
        var page = _harness.Create().Test;
        page.Url = "https://admin.google.com/";

        page.RunCommand.Execute(null);

        var candidate = Assert.Single(page.Candidates);
        Assert.True(candidate.IsResolved);
        Assert.Contains("Safari", candidate.Detail, StringComparison.Ordinal);
        Assert.Contains("this computer", candidate.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Changing_the_id_rewrites_the_references_and_keeps_the_previous_one()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "edge");
        profile.UsesLocalTarget = true;

        profile.IdDraft = "personal";
        Assert.Null(profile.IdError);
        profile.ChangeIdCommand.Execute(null);
        await _harness.Saves.RunPendingAsync();

        Assert.Equal("personal", profile.Id);
        var saved = _harness.SavedConfig();
        Assert.Equal("personal", saved.DefaultProfile);
        Assert.Equal(["edge"], saved.Profiles.Single(p => p.Id == "personal").PreviousIds);
        Assert.Contains("personal", saved.Rules.Single(r => r.Id == "ms-login").Actions.OfType<OpenAction>().Single().Profiles!);
        Assert.Equal(["personal"], _harness.Loader.LoadLocal().ProfileTargets?.Keys);
        Assert.Contains(Profile(settings, "chrome-work").FallbackChoices, c => c.Id == "personal");
    }

    [Fact]
    public void An_unusable_id_is_explained_and_cannot_be_applied()
    {
        var settings = _harness.WithSample().Create();
        var profile = Profile(settings, "edge");

        profile.IdDraft = "chrome-work";

        Assert.Contains("already", profile.IdError, StringComparison.Ordinal);
        Assert.False(profile.ChangeIdCommand.CanExecute(null));
        profile.IdDraft = "edge";
        Assert.Null(profile.IdError);
        Assert.False(profile.ChangeIdCommand.CanExecute(null));
    }

    [Fact]
    public void New_profiles_do_not_take_an_id_another_computer_may_still_know()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, """{ "profiles": [ { "id": "work", "name": "Work", "previousIds": ["profile-4"], "browser": "edge" } ] }""");
        var page = _harness.Create().Profiles;

        page.AddDetectedCommand.Execute(page.DetectedBrowsers.Single(b => b.Id == "safari").Items[0]);

        Assert.Equal("profile-5", page.SelectedProfile?.Id);
    }

    [Fact]
    public void New_profiles_do_not_take_an_id_this_computer_keeps_a_target_under()
    {
        WriteLocal("""{ "profileTargets": { "profile-6": { "browser": "safari" } } }""");
        var page = _harness.Create().Profiles;

        page.AddCommandProfileCommand.Execute(null);

        Assert.Equal("profile-7", page.SelectedProfile?.Id);
    }
}
