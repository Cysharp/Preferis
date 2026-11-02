using Preferis.Platform.MacOS;

namespace Preferis.Platform.Tests.MacOS;

public class MacBrowserDetectorTests
{
    [Theory]
    // The Router and its helpers, whichever of them is asking.
    [InlineData("com.cysharp.Preferis", "com.cysharp.Preferis", true)]
    [InlineData("com.cysharp.Preferis", "com.cysharp.Preferis.Settings", true)]
    [InlineData("com.cysharp.preferis.picker", "com.cysharp.Preferis.Settings", true)]
    [InlineData("com.cysharp.Preferis", null, true)]
    // Real browsers, and look-alikes that only share a prefix.
    [InlineData("com.google.Chrome", "com.cysharp.Preferis.Settings", false)]
    [InlineData("com.cysharp.PreferisBeta", "com.cysharp.Preferis", false)]
    public void Preferis_itself_is_never_offered_as_a_browser(string bundleId, string? selfId, bool excluded)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Equal(excluded, MacBrowserDetector.IsPreferis(bundleId, selfId));
    }
}
