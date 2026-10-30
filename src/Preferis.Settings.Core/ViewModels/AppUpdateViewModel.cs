using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Settings.Services;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>Where an update of Preferis stands.</summary>
public enum AppUpdateState
{
    /// <summary>Nothing has been checked since the settings opened.</summary>
    Idle,
    Checking,
    UpToDate,
    /// <summary>A newer version can be downloaded.</summary>
    Available,
    Downloading,
    /// <summary>Downloaded; Preferis restarts to install it.</summary>
    Ready,
    /// <summary>The check or the download failed; <see cref="AppUpdateViewModel.StatusText"/> says why.</summary>
    Failed,
}

/// <summary>
/// The "Updates" card of the general page, for an installation an installer manages (<see cref="IAppUpdater"/>): check,
/// download, then restart. It belongs to the session rather than to the page, so that a reload of <c>config.json</c>
/// (the Router remembering a picker choice replaces the page view models) does not lose a download in progress.
/// </summary>
public sealed partial class AppUpdateViewModel : ObservableObject
{
    private readonly IAppUpdater _updater;
    private readonly Func<Task<bool>> _saveBeforeRestart;

    /// <param name="saveBeforeRestart">
    /// Writes the edits still waiting, as closing the window does; false when Preferis should stay open because they
    /// could not be written (the installer would exit it without asking).
    /// </param>
    public AppUpdateViewModel(IAppUpdater updater, Func<Task<bool>> saveBeforeRestart)
    {
        _updater = updater;
        _saveBeforeRestart = saveBeforeRestart;
    }

    public string CurrentVersion => _updater.CurrentVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(DownloadCommand), nameof(RestartCommand))]
    public partial AppUpdateState State { get; private set; }

    /// <summary>What the last check found, being downloaded or waiting for the restart.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial AppUpdate? Update { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int DownloadPercent { get; private set; }

    /// <summary>Why the check or the download failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? Error { get; private set; }

    /// <summary>The line under the version; empty before the first check.</summary>
    public string StatusText => State switch
    {
        AppUpdateState.Checking => L("Checking for updates…"),
        AppUpdateState.UpToDate => L("Preferis is up to date."),
        AppUpdateState.Available => L("Version {0} is available.", Update?.Version),
        AppUpdateState.Downloading => L("Downloading version {0}… {1}%", Update?.Version, DownloadPercent),
        AppUpdateState.Ready => L("Version {0} is downloaded. Restart Preferis to finish the update.", Update?.Version),
        AppUpdateState.Failed => Error ?? "",
        _ => "",
    };

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task Check()
    {
        Error = null;
        State = AppUpdateState.Checking;
        try
        {
            Update = await _updater.CheckAsync();
            State = Update is null ? AppUpdateState.UpToDate : AppUpdateState.Available;
        }
        // The network, GitHub's API and the installer's files fail in more ways than can be listed; each is a line on the
        // card to read and try again from, not the end of the settings.
        catch (Exception ex)
        {
            Fail(L("Could not check for updates: {0}", ex.Message));
        }
    }

    private bool CanCheck => State is not (AppUpdateState.Checking or AppUpdateState.Downloading);

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task Download()
    {
        var update = Update!;
        DownloadPercent = 0;
        State = AppUpdateState.Downloading;
        try
        {
            await _updater.DownloadAsync(update, percent => DownloadPercent = percent);
            DownloadPercent = 100;
            State = AppUpdateState.Ready;
        }
        catch (Exception ex)
        {
            Fail(L("Could not download the update: {0}", ex.Message));
        }
    }

    private bool CanDownload => State == AppUpdateState.Available;

    /// <summary>Hands over to the installer once the edits are on disk; nothing happens when they cannot be saved.</summary>
    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task Restart()
    {
        if (await _saveBeforeRestart())
        {
            _updater.ApplyAndRestart(Update!);
        }
    }

    private bool CanRestart => State == AppUpdateState.Ready;

    private void Fail(string message)
    {
        Error = message;
        State = AppUpdateState.Failed;
    }
}
