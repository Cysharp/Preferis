using Preferis.Platform.MacOS;

namespace Preferis.Platform.Tests.MacOS;

public class MacUrlActivationSourceTests
{
    [Theory]
    // Launch Services names the bundle it launched.
    [InlineData("com.cysharp.Preferis", true)]
    // Inherited by shells and children of other apps that Launch Services launched.
    [InlineData("com.apple.Terminal", false)]
    [InlineData("com.anthropic.claudefordesktop", false)]
    [InlineData("com.cysharp.Preferis.Settings", false)]
    [InlineData(null, false)]
    public void Only_our_own_bundle_id_means_launch_services_started_the_router(string? cfBundleIdentifier, bool expected)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal(expected, MacUrlActivationSource.IsLaunchedByLaunchServicesWith(cfBundleIdentifier));
    }
}
