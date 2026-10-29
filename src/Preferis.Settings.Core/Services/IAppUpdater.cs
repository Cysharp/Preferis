namespace Preferis.Settings.Services;

/// <summary>A release of Preferis newer than the one installed.</summary>
public sealed record AppUpdate(string Version);

/// <summary>
/// In-app updates of an installation that an installer manages: Velopack on Windows (docs/Platform.md §2.1). The
/// portable zip, a local build and macOS have none, so <see cref="ISettingsPlatform.Updater"/> is null there and the
/// settings show nothing about updates. Faked in the tests.
/// </summary>
public interface IAppUpdater
{
    /// <summary>The version this installation runs, as the installer records it.</summary>
    string CurrentVersion { get; }

    /// <summary>The latest release newer than the installed version, or null when there is none.</summary>
    Task<AppUpdate?> CheckAsync();

    /// <summary>
    /// Downloads <paramref name="update"/> into the installation, reporting percentages (0 to 100) to
    /// <paramref name="progress"/> on the thread this was called from.
    /// </summary>
    Task DownloadAsync(AppUpdate update, Action<int> progress);

    /// <summary>Exits Preferis, installs the downloaded <paramref name="update"/> and starts Preferis again. Does not return.</summary>
    void ApplyAndRestart(AppUpdate update);
}
