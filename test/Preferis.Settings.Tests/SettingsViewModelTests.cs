using Preferis.Core.Configuration;

namespace Preferis.Settings.Tests;

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
        Assert.Equal([settings.Profiles, settings.Rules, settings.Test, settings.General], settings.Pages);
        Assert.Same(settings.Profiles, settings.SelectedPage);
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
    public void An_edit_makes_the_session_dirty_and_saving_writes_it()
    {
        var settings = _harness.WithSample().Create();

        settings.Profiles.Profiles[0].Name = "Edge (renamed)";
        Assert.True(settings.IsDirty);

        settings.SaveCommand.Execute(null);

        Assert.False(settings.IsDirty);
        Assert.Equal("Edge (renamed)", _harness.SavedConfig().Profiles[0].Name);
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
        settings.SaveCommand.Execute(null);

        Assert.Contains("fromTheFuture", File.ReadAllText(_harness.Paths.DefaultConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Revert_discards_unsaved_changes()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Edge (renamed)";

        settings.RevertCommand.Execute(null);

        Assert.False(settings.IsDirty);
        Assert.Equal("Microsoft Edge", settings.Profiles.Profiles[0].Name);
    }

    [Fact]
    public void Reloading_keeps_the_current_page()
    {
        var settings = _harness.WithSample().Create();
        settings.SelectedPage = settings.Rules;

        settings.RevertCommand.Execute(null);

        Assert.Same(settings.Rules, settings.SelectedPage);
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

        settings.SaveCommand.Execute(null);

        Assert.True(settings.HasExternalChanges);
        Assert.True(settings.IsDirty);
        Assert.Equal("Theirs", _harness.SavedConfig().Profiles[1].Name);

        settings.SaveAnywayCommand.Execute(null);

        Assert.False(settings.HasExternalChanges);
        Assert.Equal("Mine", _harness.SavedConfig().Profiles[0].Name);
        Assert.Equal("Chrome (Default)", _harness.SavedConfig().Profiles[1].Name);
    }

    [Fact]
    public void Local_only_edits_are_saved_without_touching_a_config_json_changed_meanwhile()
    {
        var settings = _harness.WithSample().Create();
        settings.General.LogLevel = "trace";
        var theirs = ConfigSerializer.Deserialize(SettingsHarness.SampleJson);
        theirs.Profiles[0].Name = "Theirs";
        _harness.Loader.Save(theirs, _harness.Paths.DefaultConfigPath);

        settings.SaveCommand.Execute(null);

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
        settings.SaveCommand.Execute(null);

        Assert.NotNull(settings.LoadError);
        Assert.True(settings.ConfigUnreadable);
        Assert.Equal("{ broken", File.ReadAllText(_harness.Paths.DefaultConfigPath));
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
    public void A_broken_file_is_reported_and_replaced_only_when_saving()
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
    public async Task Closing_with_unsaved_changes_asks_first()
    {
        var settings = _harness.WithSample().Create();
        Assert.True(await settings.CanCloseAsync());

        settings.Profiles.Profiles[0].Name = "Mine";

        Assert.False(await settings.CanCloseAsync());
        _harness.Dialogs.Answers.Enqueue("Don't Save");
        Assert.True(await settings.CanCloseAsync());
        Assert.Equal("Microsoft Edge", _harness.SavedConfig().Profiles[0].Name);

        _harness.Dialogs.Answers.Enqueue("Save");
        Assert.True(await settings.CanCloseAsync());
        Assert.Equal("Mine", _harness.SavedConfig().Profiles[0].Name);
    }
}
