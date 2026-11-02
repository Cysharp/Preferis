#if VELOPACK
using Velopack;
using Velopack.Sources;

namespace Preferis.Platform.Windows;

/// <summary>
/// Velopack's updates of an installation (docs/Platform.md §2.1) from the latest GitHub release: its
/// <c>releases.&lt;channel&gt;.json</c> and the packages it names, read through the release's download links. Velopack's
/// <see cref="GithubSource"/> would list the releases through GitHub's API, which answers 60 requests an hour from an
/// address without a token, shared by everyone behind it (an office's NAT), and fails every check after them with
/// 403 (rate limit exceeded).
/// </summary>
public sealed class LatestReleaseUpdateManager : UpdateManager
{
    /// <summary>GitHub redirects <c>releases/latest/download/&lt;file&gt;</c> to that file of the latest release.</summary>
    private const string LatestReleaseUrl = "https://github.com/Cysharp/Preferis/releases/latest/download/";

    public LatestReleaseUpdateManager()
        : base(new SimpleWebSource(LatestReleaseUrl))
    {
    }

    /// <summary>
    /// The latest release's feed holds what was packed with it alone: its full and delta packages, and the full package
    /// of the release before, which the delta was made from (build/Preferis.Build.proj). Velopack would apply that delta
    /// to an older installation as well and keep the files the delta leaves unchanged as that installation has them,
    /// without checking the result; an installation further behind takes the full package instead.
    /// </summary>
    protected override UpdateInfo CreateDeltaUpdateStrategy(VelopackAsset[] feed, VelopackAsset? latestLocalFull, VelopackAsset latestRemoteFull)
        => DeltasApply(feed, latestLocalFull)
            ? base.CreateDeltaUpdateStrategy(feed, latestLocalFull, latestRemoteFull)
            : new UpdateInfo(latestRemoteFull, false);

    /// <summary>Whether the feed's deltas start from the installed package: the feed has that package too.</summary>
    internal static bool DeltasApply(IEnumerable<VelopackAsset> feed, VelopackAsset? installedFull)
        => installedFull is { } installed && feed.Any(asset => asset.Type == VelopackAssetType.Full && asset.Version == installed.Version);
}
#endif
