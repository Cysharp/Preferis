using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

public sealed class GeneralPageTests : IDisposable
{
    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Picker_settings_are_created_on_first_change()
    {
        var settings = _harness.Create();
        var page = settings.General;
        Assert.Null(settings.Config.Settings);
        Assert.Equal(PickerPosition.Cursor, page.PickerPosition);

        page.PickerPosition = PickerPosition.Center;
        page.AutoSelectSeconds = 5;
        page.RememberChoiceDefault = true;

        var picker = settings.Config.Settings?.Picker;
        Assert.Equal("center", picker?.Position);
        Assert.Equal(5, picker?.AutoSelectSeconds);
        Assert.True(picker?.RememberChoiceDefault);
    }

    [Fact]
    public void The_language_is_stored_in_the_shared_configuration()
    {
        var settings = _harness.Create();

        settings.General.Language = settings.General.Languages.Single(l => l.Value == "ja");

        Assert.Equal("ja", settings.Config.Settings?.Language);
    }

    [Fact]
    public void Logging_belongs_to_this_machine_and_is_saved_with_the_rest()
    {
        var settings = _harness.Create();
        var page = settings.General;

        page.LogLevel = "trace";
        page.IsHistoryEnabled = true;
        page.HistorySize = 20;
        Assert.True(settings.IsDirty);
        _harness.Saves.RunPending();

        var local = _harness.Loader.LoadLocal();
        Assert.Equal("trace", local.Logging?.Level);
        Assert.True(local.Logging?.History);
        Assert.Equal(20, local.Logging?.HistorySize);
        Assert.False(settings.IsDirty);
        Assert.False(File.Exists(_harness.Paths.DefaultConfigPath));
    }

    [Fact]
    public void Shows_whether_preferis_is_the_default_browser()
    {
        var page = _harness.Create().General;

        Assert.False(page.IsDefaultBrowser);
        Assert.Equal("The default browser is Safari.", page.RegistrationText);
        Assert.True(page.RegisterCommand.CanExecute(null));
    }

    [Fact]
    public async Task Registering_runs_the_router_and_refreshes_the_status()
    {
        var page = _harness.Create().General;
        _harness.Platform.Status = new RegistrationStatus(true, true, null);

        await page.RegisterCommand.ExecuteAsync(null);

        Assert.Equal(1, _harness.Platform.RegisterCalls);
        Assert.True(page.IsDefaultBrowser);
        Assert.Equal("Preferis is the default browser.", page.RegistrationText);
    }

    [Fact]
    public void Registration_needs_the_app_bundle()
    {
        _harness.Platform.CanRegister = false;

        var page = _harness.Create().General;

        Assert.False(page.RegisterCommand.CanExecute(null));
        Assert.Contains("Preferis.app", page.RegistrationText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_the_configuration_writes_it_to_the_chosen_folder()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Unsaved edit";
        var cloud = Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName;
        _harness.Dialogs.Folders.Enqueue(cloud);

        await settings.General.MoveConfigCommand.ExecuteAsync(null);

        var moved = Path.Combine(cloud, "config.json");
        Assert.Equal(moved, settings.ConfigPath);
        Assert.Equal(moved, settings.General.ConfigPath);
        Assert.False(File.Exists(_harness.Paths.DefaultConfigPath));
        Assert.Equal("Unsaved edit", _harness.SavedConfig().Profiles[0].Name);
        Assert.False(settings.IsDirty);
    }

    [Fact]
    public async Task Moving_takes_changes_made_on_disk_meanwhile_along()
    {
        var settings = _harness.WithSample().Create();
        var theirs = ConfigSerializer.Deserialize(SettingsHarness.SampleJson);
        theirs.Profiles[0].Name = "Theirs";
        _harness.Loader.Save(theirs, _harness.Paths.DefaultConfigPath);
        var cloud = Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName;
        _harness.Dialogs.Folders.Enqueue(cloud);

        await settings.General.MoveConfigCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(cloud, "config.json"), settings.ConfigPath);
        Assert.Equal("Theirs", _harness.SavedConfig().Profiles[0].Name);
    }

    [Fact]
    public async Task Moving_waits_when_changes_on_disk_and_here_conflict()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[1].Name = "Mine";
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace("Microsoft Edge\"", "Theirs\"", StringComparison.Ordinal));
        _harness.Dialogs.Folders.Enqueue(Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName);

        await settings.General.MoveConfigCommand.ExecuteAsync(null);

        Assert.Equal(_harness.Paths.DefaultConfigPath, settings.ConfigPath);
        Assert.True(settings.HasExternalChanges);
        Assert.Contains("Theirs", File.ReadAllText(_harness.Paths.DefaultConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_onto_an_existing_configuration_offers_to_use_it()
    {
        var settings = _harness.Create();
        var cloud = Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName;
        File.WriteAllText(Path.Combine(cloud, "config.json"), SettingsHarness.SampleJson);
        _harness.Dialogs.Folders.Enqueue(cloud);
        _harness.Dialogs.Answers.Enqueue("Use It");

        await settings.General.MoveConfigCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(cloud, "config.json"), settings.ConfigPath);
        Assert.Equal(6, settings.Profiles.Profiles.Count);
    }

    [Fact]
    public async Task An_existing_file_can_be_adopted_directly()
    {
        var settings = _harness.Create();
        var synced = Path.Combine(_harness.Root, "synced.json");
        File.WriteAllText(synced, SettingsHarness.SampleJson);
        _harness.Dialogs.Files.Enqueue(synced);

        await settings.General.UseExistingConfigCommand.ExecuteAsync(null);

        Assert.Equal(synced, settings.ConfigPath);
        Assert.Equal(6, settings.Profiles.Profiles.Count);
    }

    [Fact]
    public async Task A_file_that_is_not_a_configuration_is_refused()
    {
        var settings = _harness.Create();
        var other = Path.Combine(_harness.Root, "other.json");
        File.WriteAllText(other, "[]");
        _harness.Dialogs.Files.Enqueue(other);

        await settings.General.UseExistingConfigCommand.ExecuteAsync(null);

        Assert.Equal(_harness.Paths.DefaultConfigPath, settings.ConfigPath);
        Assert.NotNull(settings.StatusMessage);
    }

    [Fact]
    public async Task The_configuration_can_go_back_to_the_default_location()
    {
        var settings = _harness.WithSample().Create();
        var cloud = Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName;
        _harness.Dialogs.Folders.Enqueue(cloud);
        await settings.General.MoveConfigCommand.ExecuteAsync(null);
        Assert.True(settings.General.MoveToDefaultLocationCommand.CanExecute(null));

        await settings.General.MoveToDefaultLocationCommand.ExecuteAsync(null);

        Assert.Equal(_harness.Paths.DefaultConfigPath, settings.ConfigPath);
        Assert.Null(_harness.Loader.LoadLocal().ConfigPath);
        Assert.False(settings.General.MoveToDefaultLocationCommand.CanExecute(null));
    }

    [Fact]
    public void Reveal_shows_the_file_in_the_file_manager()
    {
        var settings = _harness.WithSample().Create();

        settings.General.RevealConfigCommand.Execute(null);

        Assert.Equal([_harness.Paths.DefaultConfigPath], _harness.Platform.Revealed);
    }
}
