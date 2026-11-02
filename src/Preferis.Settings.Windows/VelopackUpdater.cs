using Preferis.Platform.Windows;
using Preferis.Settings.Services;
using Velopack;

namespace Preferis.Settings.Windows;

/// <summary>
/// Updates through Velopack (docs/Platform.md §2.1): the feed is the latest GitHub release's
/// <c>releases.&lt;channel&gt;.json</c> (<see cref="LatestReleaseUpdateManager"/>), the channel (win-x64 / win-arm64)
/// the one this installation was built for.
/// </summary>
internal sealed class VelopackUpdater : IAppUpdater
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _found;

    private VelopackUpdater(UpdateManager manager, string currentVersion)
    {
        _manager = manager;
        CurrentVersion = currentVersion;
    }

    /// <summary>Null outside a Velopack installation: the portable zip, or the artifacts of a local build.</summary>
    public static VelopackUpdater? TryCreate()
    {
        // UpdateManager needs the locator that VelopackInstallation.Start sets up, which it does only when installed.
        if (!VelopackInstallation.IsInstalled)
        {
            return null;
        }
        var manager = new LatestReleaseUpdateManager();
        return manager.IsInstalled && manager.CurrentVersion is { } version ? new VelopackUpdater(manager, version.ToString()) : null;
    }

    public string CurrentVersion { get; }

    public async Task<AppUpdate?> CheckAsync()
    {
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _found is null ? null : new AppUpdate(_found.TargetFullRelease.Version.ToString());
    }

    public Task DownloadAsync(AppUpdate update, Action<int> progress)
    {
        // Velopack reports from its download thread; Progress<T> brings each report back to the caller's thread.
        IProgress<int> marshalled = new Progress<int>(progress);
        return _manager.DownloadUpdatesAsync(Found(update), marshalled.Report);
    }

    public void ApplyAndRestart(AppUpdate update) => _manager.ApplyUpdatesAndRestart(Found(update));

    private UpdateInfo Found(AppUpdate update)
        => _found is { } found && found.TargetFullRelease.Version.ToString() == update.Version
            ? found
            : throw new InvalidOperationException($"Version {update.Version} is not what the last check found.");
}
