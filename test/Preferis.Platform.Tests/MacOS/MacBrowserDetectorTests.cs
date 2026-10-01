using Preferis.Platform.MacOS;

namespace Preferis.Platform.Tests.MacOS;

public class MacBrowserDetectorTests
{
    [Theory]
    // The Router and its helpers, whichever of them is asking.
    [InlineData("net.mayuki.Preferis", "net.mayuki.Preferis", true)]
    [InlineData("net.mayuki.Preferis", "net.mayuki.Preferis.Settings", true)]
    [InlineData("net.mayuki.preferis.picker", "net.mayuki.Preferis.Settings", true)]
    [InlineData("net.mayuki.Preferis", null, true)]
    // Real browsers, and look-alikes that only share a prefix.
    [InlineData("com.google.Chrome", "net.mayuki.Preferis.Settings", false)]
    [InlineData("net.mayuki.PreferisBeta", "net.mayuki.Preferis", false)]
    public void Preferis_itself_is_never_offered_as_a_browser(string bundleId, string? selfId, bool excluded)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal(excluded, MacBrowserDetector.IsPreferis(bundleId, selfId));
    }
}
