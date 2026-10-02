using Preferis.Core.Configuration;

namespace Preferis.Settings.Core.Tests;

public sealed class SettingsViewModelTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Loads_the_configuration_into_clean_pages()
    {
        var settings = _harness.WithSample().Create();

        Assert.Equal(_harness.Paths.DefaultConfigPath, settings.ConfigPath);
        Assert.False(settings.IsDirty);
        Assert.Null(settings.LoadError);
        Assert.Equal(6, settings.Profiles.Profiles.Count);
        Assert.Equal(4, settings.Rules.Rules.Count);
        Assert.Equal([settings.General, settings.Profiles, settings.Rules, settings.Test], settings.Pages);
        Assert.Same(settings.General, settings.SelectedPage);
    }

    [Fact]
    public void Without_a_file_the_default_configuration_is_edited()
    {
        var settings = _harness.Create();

        Assert.Empty(settings.Profiles.Profiles);
        Assert.False(settings.IsDirty);
        Assert.False(settings.ConfigExists);
    }

    [Fact]
    public void Edits_are_saved_after_a_pause_without_asking()
    {
        var settings = _harness.WithSample().Create();

        settings.Profiles.Profiles[0].Name = "Edge (renamed)";
        Assert.True(settings.IsDirty);
        Assert.Equal("Microsoft Edge", _harness.SavedConfig().Profiles[0].Name);

        _harness.Saves.RunPending();

        Assert.False(settings.IsDirty);
        Assert.Equal("Edge (renamed)", _harness.SavedConfig().Profiles[0].Name);
    }

    [Fact]
    public void Nothing_is_saved_without_an_edit()
    {
        _harness.WithSample().Create();

        Assert.False(_harness.Saves.HasPending);
    }

    [Fact]
    public void Undoing_an_edit_by_hand_is_not_a_change()
    {
        var settings = _harness.WithSample().Create();
        var profile = settings.Profiles.Profiles[0];
        var original = profile.Name;

        profile.Name = "Something else";
        profile.Name = original;

        Assert.False(settings.IsDirty);
    }

    [Fact]
    public void Unknown_properties_survive_a_save()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace("\"version\": 1,", "\"version\": 1, \"fromTheFuture\": { \"x\": 1 },", StringComparison.Ordinal));
        var settings = _harness.Create();

        settings.Profiles.Profiles[0].Name = "Edge (renamed)";
        _harness.Saves.RunPending();

        Assert.Contains("fromTheFuture", File.ReadAllText(_harness.Paths.DefaultConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_changed_by_someone_else_is_not_overwritten_silently()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Mine";
        // e.g. the Router remembered a picker choice, or the sync client brought a newer file.
        var theirs = ConfigSerializer.Deserialize(SettingsHarness.SampleJson);
        theirs.Profiles[1].Name = "Theirs";
        _harness.Loader.Save(theirs, _harness.Paths.DefaultConfigPath);

        _harness.Saves.RunPending();

        Assert.True(settings.HasExternalChanges);
        Assert.True(settings.IsDirty);
        Assert.Equal("Theirs", _harness.SavedConfig().Profiles[1].Name);

        // Further edits wait for the decision instead of being saved over theirs.
        settings.Profiles.Profiles[2].Name = "Mine too";
        _harness.Saves.RunPending();
        Assert.Equal("Theirs", _harness.SavedConfig().Profiles[1].Name);

        settings.KeepMineCommand.Execute(null);

        Assert.False(settings.HasExternalChanges);
        Assert.False(settings.IsDirty);
        Assert.Equal("Mine", _harness.SavedConfig().Profiles[0].Name);
        Assert.Equal("Chrome (Default)", _harness.SavedConfig().Profiles[1].Name);
        Assert.Equal("Mine too", _harness.SavedConfig().Profiles[2].Name);
    }

    [Fact]
    public void Using_theirs_drops_the_edits_that_were_waiting()
    {
        var settings = _harness.WithSample().Create();
        settings.SelectedPage = settings.Rules;
        settings.Profiles.Profiles[0].Name = "Mine";
        var theirs = ConfigSerializer.Deserialize(SettingsHarness.SampleJson);
        theirs.Profiles[1].Name = "Theirs";
        _harness.Loader.Save(theirs, _harness.Paths.DefaultConfigPath);
        _harness.Saves.RunPending();

        settings.UseTheirsCommand.Execute(null);

        Assert.False(settings.HasExternalChanges);
        Assert.False(settings.IsDirty);
        Assert.Equal("Microsoft Edge", settings.Profiles.Profiles[0].Name);
        Assert.Equal("Theirs", settings.Profiles.Profiles[1].Name);
        Assert.Same(settings.Rules, settings.SelectedPage);
        Assert.False(_harness.Saves.HasPending);
    }

    [Fact]
    public void Local_only_edits_are_saved_without_touching_a_config_json_changed_meanwhile()
    {
        var settings = _harness.WithSample().Create();
        settings.General.LogLevel = "trace";
        var theirs = ConfigSerializer.Deserialize(SettingsHarness.SampleJson);
        theirs.Profiles[0].Name = "Theirs";
        _harness.Loader.Save(theirs, _harness.Paths.DefaultConfigPath);

        _harness.Saves.RunPending();

        Assert.False(settings.HasExternalChanges);
        Assert.False(settings.IsDirty);
        Assert.Equal("trace", _harness.Loader.LoadLocal().Logging?.Level);
        Assert.Equal("Theirs", _harness.SavedConfig().Profiles[0].Name);
        Assert.Equal("Theirs", settings.Profiles.Profiles[0].Name);
    }

    [Fact]
    public void Saving_local_settings_does_not_pretend_a_broken_config_was_fixed()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, "{ broken");
        var settings = _harness.Create();

        settings.General.LogLevel = "trace";
        _harness.Saves.RunPending();

        Assert.NotNull(settings.LoadError);
        Assert.True(settings.ConfigUnreadable);
        Assert.Equal("{ broken", File.ReadAllText(_harness.Paths.DefaultConfigPath));
        Assert.Equal("trace", _harness.Loader.LoadLocal().Logging?.Level);
    }

    [Fact]
    public void A_broken_file_is_replaced_only_when_asked()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, "{ broken");
        var settings = _harness.Create();

        settings.General.AutoSelectSeconds = 5;
        _harness.Saves.RunPending();

        Assert.True(settings.IsHoldingEditsForUnreadableFile);
        Assert.Equal("{ broken", File.ReadAllText(_harness.Paths.DefaultConfigPath));

        settings.KeepMineCommand.Execute(null);

        Assert.False(settings.IsHoldingEditsForUnreadableFile);
        Assert.False(settings.ConfigUnreadable);
        Assert.Equal(5, _harness.SavedConfig().Settings?.Picker?.AutoSelectSeconds);
    }

    [Fact]
    public void External_changes_are_picked_up_when_there_is_nothing_to_lose()
    {
        var settings = _harness.WithSample().Create();
        var theirs = ConfigSerializer.Deserialize(SettingsHarness.SampleJson);
        theirs.Profiles[0].Name = "Theirs";
        _harness.Loader.Save(theirs, _harness.Paths.DefaultConfigPath);

        settings.CheckForExternalChanges();

        Assert.False(settings.HasExternalChanges);
        Assert.Equal("Theirs", settings.Profiles.Profiles[0].Name);
    }

    [Fact]
    public void External_changes_are_flagged_when_there_are_local_edits()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Mine";
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace("Chrome (Default)", "Theirs", StringComparison.Ordinal));

        settings.CheckForExternalChanges();

        Assert.True(settings.HasExternalChanges);
        Assert.Equal("Mine", settings.Profiles.Profiles[0].Name);
    }

    [Fact]
    public void A_broken_file_is_reported_and_left_alone()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, "{ broken");

        var settings = _harness.Create();

        Assert.NotNull(settings.LoadError);
        Assert.Equal("{ broken", File.ReadAllText(_harness.Paths.DefaultConfigPath));
    }

    [Fact]
    public void Issues_follow_the_edits()
    {
        var settings = _harness.WithSample().Create();
        Assert.Empty(settings.Issues);

        settings.Profiles.Profiles[0].ExtraArgs = "--new-window";
        settings.General.LogLevel = "trace";
        settings.Profiles.SelectedProfile = settings.Profiles.Profiles[5];
        settings.Profiles.SelectedProfile.MacOSCommandPath = "";
        settings.Profiles.SelectedProfile.WindowsCommandPath = "";

        var issue = Assert.Single(settings.Issues);
        Assert.Equal("custom-tool", issue.Id);
        Assert.Equal("1 error", settings.IssueSummary);
    }

    [Fact]
    public async Task Closing_saves_the_edits_still_waiting()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Mine";

        Assert.True(await settings.CanCloseAsync());

        Assert.Empty(_harness.Dialogs.Asked);
        Assert.Equal("Mine", _harness.SavedConfig().Profiles[0].Name);
    }

    [Fact]
    public async Task Closing_asks_only_when_the_edits_cannot_be_saved()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Mine";
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace("Chrome (Default)", "Theirs", StringComparison.Ordinal));

        Assert.False(await settings.CanCloseAsync());
        _harness.Dialogs.Answers.Enqueue("Discard My Changes");
        Assert.True(await settings.CanCloseAsync());
        Assert.Equal("Theirs", _harness.SavedConfig().Profiles[1].Name);
        Assert.Equal("Microsoft Edge", _harness.SavedConfig().Profiles[0].Name);

        _harness.Dialogs.Answers.Enqueue("Keep Mine");
        Assert.True(await settings.CanCloseAsync());
        Assert.Equal("Mine", _harness.SavedConfig().Profiles[0].Name);
    }
}
