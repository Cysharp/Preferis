using Preferis.Core.Browsers;

namespace Preferis.Core.Tests.Browsers;

public class BrowserCatalogTests
{
    [Fact]
    public void Ids_are_unique()
    {
        var ids = BrowserCatalog.Entries.Select(e => e.Id).ToList();

        Assert.Equal(ids.Distinct(StringComparer.Ordinal).Count(), ids.Count);
    }

    [Theory]
    [InlineData("edge", BrowserFamily.Chromium, "Microsoft Edge", "com.microsoft.edgemac")]
    [InlineData("edge-dev", BrowserFamily.Chromium, "Microsoft Edge Dev", "com.microsoft.edgemac.Dev")]
    [InlineData("chrome", BrowserFamily.Chromium, "Google Chrome", "com.google.Chrome")]
    [InlineData("chrome-canary", BrowserFamily.Chromium, null, "com.google.Chrome.canary")]
    [InlineData("firefox", BrowserFamily.Firefox, null, "org.mozilla.firefox")]
    [InlineData("safari", BrowserFamily.Other, null, "com.apple.Safari")]
    public void Known_browsers_carry_their_detection_hints(string id, BrowserFamily family, string? windowsKey, string macBundleId)
    {
        var entry = BrowserCatalog.Find(id);

        Assert.NotNull(entry);
        Assert.Equal(family, entry.Family);
        Assert.Equal(windowsKey, entry.WindowsStartMenuKey);
        Assert.Equal(macBundleId, entry.MacBundleId);
    }

    [Fact]
    public void Channels_have_distinct_user_data_directories()
    {
        var edge = BrowserCatalog.Find("edge")!;
        var edgeDev = BrowserCatalog.Find("edge-dev")!;
        var edgeCanary = BrowserCatalog.Find("edge-canary")!;

        Assert.Equal(@"Microsoft\Edge\User Data", edge.WindowsUserDataDir);
        Assert.Equal(@"Microsoft\Edge Dev\User Data", edgeDev.WindowsUserDataDir);
        Assert.Equal(@"Microsoft\Edge SxS\User Data", edgeCanary.WindowsUserDataDir);
        Assert.Equal("Microsoft Edge", edge.MacUserDataDir);
        Assert.Equal("Microsoft Edge Dev", edgeDev.MacUserDataDir);
        Assert.Equal("Microsoft Edge Canary", edgeCanary.MacUserDataDir);
    }

    [Fact]
    public void Private_mode_arguments_differ_per_browser()
    {
        Assert.Equal("--inprivate", BrowserCatalog.Find("edge")!.PrivateModeArgument);
        Assert.Equal("--incognito", BrowserCatalog.Find("chrome")!.PrivateModeArgument);
        Assert.Equal("-private-window", BrowserCatalog.Find("firefox")!.PrivateModeArgument);
        Assert.Null(BrowserCatalog.Find("safari")!.PrivateModeArgument);
    }

    [Fact]
    public void Unknown_ids_are_not_in_the_catalog_but_platform_prefixed_ids_are_recognized()
    {
        Assert.Null(BrowserCatalog.Find("netscape"));
        Assert.True(BrowserCatalog.IsPlatformSpecificId("mac:com.example.Browser"));
        Assert.True(BrowserCatalog.IsPlatformSpecificId("win:Contoso Browser"));
        Assert.False(BrowserCatalog.IsPlatformSpecificId("edge"));
    }
}
