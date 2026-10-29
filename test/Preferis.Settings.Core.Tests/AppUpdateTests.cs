using System.Net.Http;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

public sealed class AppUpdateTests : IDisposable
{
    private readonly SettingsHarness _harness = new();
    private readonly FakeAppUpdater _updater = new();

    public AppUpdateTests() => _harness.Platform.Updater = _updater;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Without_an_installer_the_settings_offer_no_updates()
    {
        _harness.Platform.Updater = null;

        Assert.Null(_harness.Create().Updates);
    }

    [Fact]
    public void Shows_the_installed_version_before_anything_is_checked()
    {
        var updates = _harness.Create().Updates!;

        Assert.Equal("1.0.0", updates.CurrentVersion);
        Assert.Equal(AppUpdateState.Idle, updates.State);
        Assert.Equal("", updates.StatusText);
        Assert.True(updates.CheckCommand.CanExecute(null));
        Assert.False(updates.DownloadCommand.CanExecute(null));
        Assert.False(updates.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Checking_finds_nothing_when_up_to_date()
    {
        var updates = _harness.Create().Updates!;

        await updates.CheckCommand.ExecuteAsync(null);

        Assert.Equal(1, _updater.Checks);
        Assert.Equal(AppUpdateState.UpToDate, updates.State);
        Assert.Equal("Preferis is up to date.", updates.StatusText);
        Assert.False(updates.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Checking_finds_a_newer_version()
    {
        _updater.Available = new AppUpdate("1.2.3");
        var updates = _harness.Create().Updates!;

        await updates.CheckCommand.ExecuteAsync(null);

        Assert.Equal(AppUpdateState.Available, updates.State);
        Assert.Equal("Version 1.2.3 is available.", updates.StatusText);
        Assert.True(updates.DownloadCommand.CanExecute(null));
        Assert.False(updates.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Checking_shows_while_it_runs()
    {
        _updater.CheckGate = new TaskCompletionSource();
        var updates = _harness.Create().Updates!;

        var checking = updates.CheckCommand.ExecuteAsync(null);

        Assert.Equal(AppUpdateState.Checking, updates.State);
        Assert.Equal("Checking for updates…", updates.StatusText);
        Assert.False(updates.CheckCommand.CanExecute(null));
        _updater.CheckGate.SetResult();
        await checking;
        Assert.Equal(AppUpdateState.UpToDate, updates.State);
        Assert.True(updates.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_failed_check_says_why_and_can_be_repeated()
    {
        _updater.CheckError = new HttpRequestException("Response status code does not indicate success: 404 (Not Found).");
        var updates = _harness.Create().Updates!;

        await updates.CheckCommand.ExecuteAsync(null);

        Assert.Equal(AppUpdateState.Failed, updates.State);
        Assert.Equal("Could not check for updates: Response status code does not indicate success: 404 (Not Found).", updates.StatusText);
        Assert.True(updates.CheckCommand.CanExecute(null));
        Assert.False(updates.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task Downloading_reports_its_progress_and_then_waits_for_the_restart()
    {
        _updater.Available = new AppUpdate("1.2.3");
        _updater.DownloadSteps = [30, 60];
        var updates = _harness.Create().Updates!;
        await updates.CheckCommand.ExecuteAsync(null);
        var shown = new List<string>();
        updates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppUpdateViewModel.StatusText))
            {
                shown.Add(updates.StatusText);
            }
        };

        await updates.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(["1.2.3"], _updater.Downloaded);
        Assert.Contains("Downloading version 1.2.3… 30%", shown);
        Assert.Contains("Downloading version 1.2.3… 60%", shown);
        Assert.Equal(AppUpdateState.Ready, updates.State);
        Assert.Equal(100, updates.DownloadPercent);
        Assert.Equal("Version 1.2.3 is downloaded. Restart Preferis to finish the update.", updates.StatusText);
        Assert.True(updates.RestartCommand.CanExecute(null));
        Assert.False(updates.DownloadCommand.CanExecute(null));
        Assert.Null(_updater.Applied);
    }

    [Fact]
    public async Task A_failed_download_says_why()
    {
        _updater.Available = new AppUpdate("1.2.3");
        _updater.DownloadError = new IOException("There is not enough space on the disk.");
        var updates = _harness.Create().Updates!;
        await updates.CheckCommand.ExecuteAsync(null);

        await updates.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(AppUpdateState.Failed, updates.State);
        Assert.Equal("Could not download the update: There is not enough space on the disk.", updates.StatusText);
        Assert.True(updates.CheckCommand.CanExecute(null));
        Assert.False(updates.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restarting_saves_the_edits_first_and_hands_over_to_the_installer()
    {
        _updater.Available = new AppUpdate("1.2.3");
        var settings = _harness.WithSample().Create();
        var updates = settings.Updates!;
        await updates.CheckCommand.ExecuteAsync(null);
        await updates.DownloadCommand.ExecuteAsync(null);
        settings.Profiles.Profiles[0].Name = "Renamed before the update";

        await updates.RestartCommand.ExecuteAsync(null);

        Assert.Equal("Renamed before the update", _harness.SavedConfig().Profiles[0].Name);
        Assert.Equal("1.2.3", _updater.Applied);
    }

    [Fact]
    public async Task Restarting_waits_when_the_edits_cannot_be_saved()
    {
        _updater.Available = new AppUpdate("1.2.3");
        var settings = _harness.WithSample().Create();
        var updates = settings.Updates!;
        await updates.CheckCommand.ExecuteAsync(null);
        await updates.DownloadCommand.ExecuteAsync(null);
        settings.Profiles.Profiles[1].Name = "Mine";
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace("Microsoft Edge\"", "Theirs\"", StringComparison.Ordinal));
        _harness.Dialogs.Answers.Enqueue("Cancel");

        await updates.RestartCommand.ExecuteAsync(null);

        Assert.Null(_updater.Applied);
        Assert.Single(_harness.Dialogs.Asked);
        Assert.Equal(AppUpdateState.Ready, updates.State);
        Assert.True(updates.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_update_in_progress_survives_a_reload_of_the_configuration()
    {
        _updater.Available = new AppUpdate("1.2.3");
        var settings = _harness.WithSample().Create();
        var updates = settings.Updates!;
        await updates.CheckCommand.ExecuteAsync(null);
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson.Replace("Microsoft Edge\"", "Theirs\"", StringComparison.Ordinal));

        settings.CheckForExternalChanges();

        Assert.Equal("Theirs", settings.Profiles.Profiles[0].Name);
        Assert.Same(updates, settings.Updates);
        Assert.Equal(AppUpdateState.Available, updates.State);
    }
}
