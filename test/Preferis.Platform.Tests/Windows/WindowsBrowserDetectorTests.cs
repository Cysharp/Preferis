using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Platform.Windows;

namespace Preferis.Platform.Tests.Windows;

public class WindowsBrowserDetectorTests
{
    private const string Hklm = @"HKLM\SOFTWARE";
    private const string Wow = @"HKLM\SOFTWARE\WOW6432Node";
    private const string Hkcu = @"HKCU\Software";
    private const string Local = @"C:\Users\me\AppData\Local";
    private const string Roaming = @"C:\Users\me\AppData\Roaming";
    private const string Edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
    private const string EdgeDev = @"C:\Program Files (x86)\Microsoft\Edge Dev\Application\msedge.exe";

    private readonly FakeRegistry _registry = new();
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, BrowserOverride>? _overrides;

    private WindowsBrowserDetector Detector() => new(
        _registry,
        _files.Contains,
        name => name switch
        {
            "LOCALAPPDATA" => Local,
            "APPDATA" => Roaming,
            "ProgramFiles" => @"C:\Program Files",
            _ => null,
        },
        Local,
        Roaming,
        _overrides);

    private WindowsBrowserDetectorTests WithBrowser(string root, string key, string executable, string? command = null, string? applicationName = null)
    {
        _registry.WithBrowser(root, key, command ?? $"\"{executable}\"", applicationName);
        _files.Add(executable);
        return this;
    }

    [Fact]
    public void Catalog_browsers_are_recognized_by_their_key()
    {
        WithBrowser(Hklm, "Microsoft Edge", Edge, applicationName: "Microsoft Edge");
        WithBrowser(Hklm, "Microsoft Edge Dev", EdgeDev, applicationName: "Microsoft Edge Dev");

        var browsers = Detector().DetectBrowsers();

        Assert.Collection(browsers,
            edge =>
            {
                Assert.Equal("edge", edge.Id);
                Assert.Equal("Microsoft Edge", edge.DisplayName);
                Assert.Equal(BrowserFamily.Chromium, edge.Family);
                Assert.Equal(Edge, edge.ExecutablePath);
                Assert.Null(edge.ApplicationPath);
                Assert.Equal(Path.Combine(Local, @"Microsoft\Edge\User Data"), edge.UserDataDir);
            },
            dev =>
            {
                Assert.Equal("edge-dev", dev.Id);
                Assert.Equal(EdgeDev, dev.ExecutablePath);
                Assert.Equal(Path.Combine(Local, @"Microsoft\Edge Dev\User Data"), dev.UserDataDir);
            });
    }

    [Fact]
    public void A_per_user_install_wins_over_the_machine_wide_one()
    {
        var userChrome = Local + @"\Google\Chrome\Application\chrome.exe";
        WithBrowser(Hklm, "Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        WithBrowser(Hkcu, "Google Chrome", userChrome);

        var chrome = Assert.Single(Detector().DetectBrowsers());

        Assert.Equal(userChrome, chrome.ExecutablePath);
    }

    [Fact]
    public void Keys_registered_by_32_bit_installers_are_found()
    {
        WithBrowser(Wow, "Microsoft Edge", Edge);

        Assert.Equal("edge", Assert.Single(Detector().DetectBrowsers()).Id);
    }

    [Fact]
    public void Per_user_keys_with_an_installation_suffix_are_recognized()
    {
        // Chrome appends a suffix to its key for per-user installs alongside a machine-wide one.
        var userChrome = Local + @"\Google\Chrome\Application\chrome.exe";
        WithBrowser(Hkcu, "Google Chrome.QQG3HJ2ZRSJWIV6KJ4HFOKYJWE", userChrome);

        var chrome = Assert.Single(Detector().DetectBrowsers());

        Assert.Equal("chrome", chrome.Id);
        Assert.Equal(Path.Combine(Local, @"Google\Chrome\User Data"), chrome.UserDataDir);
    }

    [Fact]
    public void Firefox_channels_are_told_apart_by_the_prefix_of_their_hashed_keys()
    {
        WithBrowser(Hklm, "Firefox-308046B0AF4A39CB", @"C:\Program Files\Mozilla Firefox\firefox.exe", "\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\" -osint -url \"%1\"");
        WithBrowser(Hklm, "Firefox Developer Edition-E7CF176E110C211B", @"C:\Program Files\Firefox Developer Edition\firefox.exe");
        WithBrowser(Hklm, "Firefox Nightly-6F193CCC56814779", @"C:\Program Files\Firefox Nightly\firefox.exe");

        var browsers = Detector().DetectBrowsers();

        Assert.Equal(["firefox", "firefox-developer", "firefox-nightly"], browsers.Select(b => b.Id));
        Assert.Equal(@"C:\Program Files\Mozilla Firefox\firefox.exe", browsers[0].ExecutablePath);
        Assert.All(browsers, b => Assert.Equal(Path.Combine(Roaming, @"Mozilla\Firefox"), b.UserDataDir));
        Assert.All(browsers, b => Assert.Equal(BrowserFamily.Firefox, b.Family));
    }

    [Fact]
    public void The_legacy_firefox_key_is_firefox()
    {
        WithBrowser(Hklm, "FIREFOX.EXE", @"C:\Program Files\Mozilla Firefox\firefox.exe");

        Assert.Equal("firefox", Assert.Single(Detector().DetectBrowsers()).Id);
    }

    [Fact]
    public void An_unknown_key_of_a_known_executable_is_matched_by_its_install_folder()
    {
        WithBrowser(Hkcu, "Edge Beta (custom)", @"C:\Program Files (x86)\Microsoft\Edge Beta\Application\msedge.exe");
        WithBrowser(Hkcu, "Some Edge", @"D:\Portable\Edge\msedge.exe");

        var browsers = Detector().DetectBrowsers();

        Assert.Equal(["edge", "edge-beta"], browsers.Select(b => b.Id));
        Assert.Equal(@"D:\Portable\Edge\msedge.exe", browsers[0].ExecutablePath);
    }

    [Fact]
    public void Browsers_outside_the_catalog_get_a_win_id()
    {
        WithBrowser(Hkcu, "Contoso", @"C:\Contoso\contoso.exe", applicationName: "Contoso Browser");
        WithBrowser(Hkcu, "Fabrikam", @"C:\Fabrikam\fab.exe", applicationName: @"@C:\Fabrikam\fab.exe,-101");

        var browsers = Detector().DetectBrowsers();

        Assert.Collection(browsers,
            contoso =>
            {
                Assert.Equal("win:Contoso", contoso.Id);
                Assert.Equal("Contoso Browser", contoso.DisplayName);
                Assert.Equal(BrowserFamily.Other, contoso.Family);
                Assert.Null(contoso.UserDataDir);
                Assert.Null(contoso.CatalogEntry);
            },
            // A resource reference is not a name to show; the key's default value is.
            fabrikam => Assert.Equal("Fabrikam", fabrikam.DisplayName));
    }

    [Fact]
    public void Preferis_and_internet_explorer_are_not_offered()
    {
        WithBrowser(Hkcu, "Preferis", @"C:\Tools\Preferis\Preferis.exe");
        WithBrowser(Hkcu, "Preferis Dev", @"D:\src\Preferis\artifacts\win-x64\Preferis\Preferis.exe");
        // Retired on Windows 11; starting it opens Edge.
        WithBrowser(Hklm, "IEXPLORE.EXE", @"C:\Program Files\Internet Explorer\iexplore.exe", @"C:\Program Files\Internet Explorer\iexplore.exe");

        Assert.Empty(Detector().DetectBrowsers());
    }

    [Fact]
    public void Keys_whose_executable_is_gone_are_skipped()
    {
        _registry.WithBrowser(Hklm, "Microsoft Edge", $"\"{Edge}\"");

        Assert.Empty(Detector().DetectBrowsers());
    }

    [Fact]
    public void Chrome_canary_is_found_at_its_install_location()
    {
        var canary = Local + @"\Google\Chrome SxS\Application\chrome.exe";
        _files.Add(canary);

        var browser = Assert.Single(Detector().DetectBrowsers());

        Assert.Equal("chrome-canary", browser.Id);
        Assert.Equal(canary, browser.ExecutablePath);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Mozilla Firefox\\firefox.exe\" -osint -url \"%1\"", @"C:\Program Files\Mozilla Firefox\firefox.exe")]
    [InlineData(@"C:\Program Files\Internet Explorer\iexplore.exe", @"C:\Program Files\Internet Explorer\iexplore.exe")]
    [InlineData(@"C:\Program Files\Contoso\contoso.EXE --new-window", @"C:\Program Files\Contoso\contoso.EXE")]
    [InlineData(@"%ProgramFiles%\Contoso\contoso.exe", @"C:\Program Files\Contoso\contoso.exe")]
    [InlineData("", null)]
    public void The_executable_is_taken_from_the_open_command(string command, string? expected)
    {
        Assert.Equal(expected, WindowsBrowserDetector.ExecutableFromCommand(command, name => name == "ProgramFiles" ? @"C:\Program Files" : null));
    }

    [Fact]
    public void Resolving_one_catalog_browser_reads_only_its_keys()
    {
        WithBrowser(Hklm, "Microsoft Edge", Edge);
        WithBrowser(Hklm, "Microsoft Edge Dev", EdgeDev);
        WithBrowser(Hklm, "Contoso", @"C:\Contoso\contoso.exe");

        var dev = Detector().TryResolve("edge-dev");

        Assert.Equal(EdgeDev, dev?.ExecutablePath);
        Assert.DoesNotContain(_registry.Reads, r => r.Contains(@"\Contoso", StringComparison.Ordinal) || r.Contains(@"Microsoft Edge\", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolving_finds_suffixed_keys_and_unknown_ids()
    {
        WithBrowser(Hkcu, "Google Chrome.QQG3HJ2ZRSJWIV6KJ4HFOKYJWE", Local + @"\Google\Chrome\Application\chrome.exe");
        WithBrowser(Hklm, "Firefox-308046B0AF4A39CB", @"C:\Program Files\Mozilla Firefox\firefox.exe");
        WithBrowser(Hkcu, "Contoso", @"C:\Contoso\contoso.exe");
        var detector = Detector();

        Assert.Equal("chrome", detector.TryResolve("chrome")?.Id);
        Assert.Equal("firefox", detector.TryResolve("firefox")?.Id);
        Assert.Equal(@"C:\Contoso\contoso.exe", detector.TryResolve("win:Contoso")?.ExecutablePath);
        Assert.Null(detector.TryResolve("edge"));
        Assert.Null(detector.TryResolve("safari"));
        Assert.Null(detector.TryResolve("mac:com.apple.Safari"));
    }

    [Fact]
    public void Overrides_from_local_json_take_precedence()
    {
        WithBrowser(Hklm, "Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        var portable = @"D:\Portable\Chrome\chrome.exe";
        _files.Add(portable);
        _overrides = new() { ["chrome"] = new BrowserOverride { Path = portable, UserDataDir = @"D:\Portable\Chrome\Data" } };
        var detector = Detector();

        var resolved = detector.TryResolve("chrome");
        var detected = Assert.Single(detector.DetectBrowsers());

        Assert.Equal(portable, resolved?.ExecutablePath);
        Assert.Equal(@"D:\Portable\Chrome\Data", resolved?.UserDataDir);
        Assert.Equal(portable, detected.ExecutablePath);
    }

    [Fact]
    public void An_override_makes_a_browser_without_a_key_available()
    {
        var portable = @"D:\Portable\Brave\brave.exe";
        _files.Add(portable);
        _overrides = new() { ["brave"] = new BrowserOverride { Path = portable } };

        var brave = Detector().TryResolve("brave");

        Assert.Equal(portable, brave?.ExecutablePath);
        Assert.Equal(Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data"), brave?.UserDataDir);
    }

    [Fact]
    public void Detected_browsers_follow_the_catalog_order_then_the_rest_by_id()
    {
        WithBrowser(Hkcu, "Zeta", @"C:\Zeta\zeta.exe");
        WithBrowser(Hklm, "Firefox-308046B0AF4A39CB", @"C:\Program Files\Mozilla Firefox\firefox.exe");
        WithBrowser(Hkcu, "Alpha", @"C:\Alpha\alpha.exe");
        WithBrowser(Hklm, "Microsoft Edge", Edge);

        Assert.Equal(["edge", "firefox", "win:Alpha", "win:Zeta"], Detector().DetectBrowsers().Select(b => b.Id));
    }
}
