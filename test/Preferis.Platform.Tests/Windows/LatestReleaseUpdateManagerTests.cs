#if VELOPACK
using Preferis.Platform.Windows;
using Velopack;

namespace Preferis.Platform.Tests.Windows;

public class LatestReleaseUpdateManagerTests
{
    // The feed of a release as PackageWindows packs it: the release before it, fetched for the delta, and this one.
    private static readonly VelopackAsset[] Feed = [Asset("1.1.0", VelopackAssetType.Full), Asset("1.1.0", VelopackAssetType.Delta), Asset("1.0.0", VelopackAssetType.Full)];

    [Fact]
    public void The_release_before_takes_the_delta()
    {
        Assert.True(LatestReleaseUpdateManager.DeltasApply(Feed, Asset("1.0.0", VelopackAssetType.Full)));
    }

    [Fact]
    public void An_installation_further_behind_takes_the_full_package()
    {
        Assert.False(LatestReleaseUpdateManager.DeltasApply(Feed, Asset("0.9.0", VelopackAssetType.Full)));
    }

    [Fact]
    public void Without_an_installed_package_there_is_no_delta()
    {
        Assert.False(LatestReleaseUpdateManager.DeltasApply(Feed, null));
    }

    private static VelopackAsset Asset(string version, VelopackAssetType type) => new()
    {
        PackageId = "Preferis",
        Version = SemanticVersion.Parse(version),
        Type = type,
        FileName = $"Preferis-{version}-win-x64-{type.ToString().ToLowerInvariant()}.nupkg",
    };
}
#endif
