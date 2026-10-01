namespace Preferis.Core.Browsers;

/// <summary>
/// The known browsers. Ids are the only form a configuration may reference a browser by, apart from the
/// platform specific escape hatches <c>win:&lt;StartMenuInternet key&gt;</c> and <c>mac:&lt;bundle id&gt;</c>.
/// Exact values are verified on real machines and recorded in docs/Platform.md.
/// </summary>
public static class BrowserCatalog
{
    private const string Chromium = "--incognito";

    public static IReadOnlyList<BrowserCatalogEntry> Entries { get; } =
    [
        Edge("edge", "Microsoft Edge", "Microsoft Edge", "com.microsoft.edgemac", @"Microsoft\Edge\User Data", "Microsoft Edge"),
        Edge("edge-beta", "Microsoft Edge Beta", "Microsoft Edge Beta", "com.microsoft.edgemac.Beta", @"Microsoft\Edge Beta\User Data", "Microsoft Edge Beta"),
        Edge("edge-dev", "Microsoft Edge Dev", "Microsoft Edge Dev", "com.microsoft.edgemac.Dev", @"Microsoft\Edge Dev\User Data", "Microsoft Edge Dev"),
        Edge("edge-canary", "Microsoft Edge Canary", "Microsoft Edge Canary", "com.microsoft.edgemac.Canary", @"Microsoft\Edge SxS\User Data", "Microsoft Edge Canary"),

        Chrome("chrome", "Google Chrome", "Google Chrome", [], "com.google.Chrome", @"Google\Chrome\User Data", "Google/Chrome"),
        Chrome("chrome-beta", "Google Chrome Beta", "Google Chrome Beta", [], "com.google.Chrome.beta", @"Google\Chrome Beta\User Data", "Google/Chrome Beta"),
        Chrome("chrome-dev", "Google Chrome Dev", "Google Chrome Dev", [], "com.google.Chrome.dev", @"Google\Chrome Dev\User Data", "Google/Chrome Dev"),
        // Canary cannot be the default browser on Windows and therefore does not register under StartMenuInternet.
        Chrome("chrome-canary", "Google Chrome Canary", null, [@"%LOCALAPPDATA%\Google\Chrome SxS\Application\chrome.exe"], "com.google.Chrome.canary", @"Google\Chrome SxS\User Data", "Google/Chrome Canary"),

        new("brave", "Brave", BrowserFamily.Chromium, "Brave", null, ["brave.exe"], [], WindowsKnownFolder.LocalAppData, @"BraveSoftware\Brave-Browser\User Data", "com.brave.Browser", "BraveSoftware/Brave-Browser", Chromium),
        new("vivaldi", "Vivaldi", BrowserFamily.Chromium, "Vivaldi", null, ["vivaldi.exe"], [], WindowsKnownFolder.LocalAppData, @"Vivaldi\User Data", "com.vivaldi.Vivaldi", "Vivaldi", Chromium),
        // Opera keeps a single profile; its user data layout is not enumerated.
        new("opera", "Opera", BrowserFamily.Chromium, "OperaStable", null, ["opera.exe", "launcher.exe"], [], WindowsKnownFolder.RoamingAppData, null, "com.operasoftware.Opera", null, "--private"),
        // Arc's Windows user data location is still to be confirmed.
        new("arc", "Arc", BrowserFamily.Chromium, "Arc", null, ["Arc.exe"], [], WindowsKnownFolder.LocalAppData, null, "company.thebrowser.Browser", "Arc/User Data", Chromium),

        Firefox("firefox", "Mozilla Firefox", "Firefox-", "org.mozilla.firefox"),
        Firefox("firefox-developer", "Firefox Developer Edition", "Firefox Developer Edition-", "org.mozilla.firefoxdeveloperedition"),
        Firefox("firefox-nightly", "Firefox Nightly", "Firefox Nightly-", "org.mozilla.nightly"),

        new("safari", "Safari", BrowserFamily.Other, null, null, [], [], WindowsKnownFolder.LocalAppData, null, "com.apple.Safari", null, null),
    ];

    private static readonly Dictionary<string, BrowserCatalogEntry> ById = Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);

    public static BrowserCatalogEntry? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>True for <c>win:</c> / <c>mac:</c> prefixed ids that bypass the catalog.</summary>
    public static bool IsPlatformSpecificId(string id)
        => id.StartsWith("win:", StringComparison.Ordinal) || id.StartsWith("mac:", StringComparison.Ordinal);

    private static BrowserCatalogEntry Edge(string id, string name, string key, string bundleId, string winUserData, string macUserData)
        => new(id, name, BrowserFamily.Chromium, key, null, ["msedge.exe"], [], WindowsKnownFolder.LocalAppData, winUserData, bundleId, macUserData, "--inprivate");

    private static BrowserCatalogEntry Chrome(string id, string name, string? key, IReadOnlyList<string> knownPaths, string bundleId, string winUserData, string macUserData)
        => new(id, name, BrowserFamily.Chromium, key, null, ["chrome.exe"], knownPaths, WindowsKnownFolder.LocalAppData, winUserData, bundleId, macUserData, Chromium);

    // All Firefox channels share profiles.ini; installs.ini maps each install to its default profile.
    private static BrowserCatalogEntry Firefox(string id, string name, string keyPrefix, string bundleId)
        => new(id, name, BrowserFamily.Firefox, null, keyPrefix, ["firefox.exe"], [], WindowsKnownFolder.RoamingAppData, @"Mozilla\Firefox", bundleId, "Firefox", "-private-window");
}
