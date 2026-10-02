using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Localization;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Hosting;

/// <summary>In-memory stand-ins for everything the Router talks to. Records every side effect.</summary>
internal sealed class FakeHost
{
    public PreferisConfig Config { get; set; } = PreferisConfig.CreateDefault();
    public LocalConfig Local { get; set; } = new();
    public string? ConfigError { get; set; }
    public bool ConfigExists { get; set; } = true;
    public PreferisConfig? SavedConfig { get; private set; }

    public Dictionary<string, DetectedBrowser> Browsers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, IReadOnlyList<BrowserProfileInfo>?> BrowserProfiles { get; } = new(StringComparer.Ordinal);

    public List<LaunchPlan> Launched { get; } = [];
    public HashSet<string> FailingLaunchPaths { get; } = new(StringComparer.Ordinal);

    public List<PickerRequest> PickerRequests { get; } = [];
    public Func<PickerRequest, PickerResponse?> PickerAnswer { get; set; } = _ => null;

    public bool SelfIsDefaultHandler { get; set; }
    public List<RoutingUrl> OpenedWithSystem { get; } = [];

    public List<(string Title, string Message, RoutingUrl? Url)> Errors { get; } = [];
    public List<(LogLevel Level, string Message)> Log { get; } = [];

    public FakeHost WithSampleConfig()
    {
        Config = ConfigSerializer.Deserialize(Fixtures.ConfigSample);
        return this;
    }

    public FakeHost WithBrowser(string id, string executable, string? app = null)
    {
        var entry = BrowserCatalog.Find(id);
        Browsers[id] = new DetectedBrowser(id, entry?.DisplayName ?? id, entry?.Family ?? BrowserFamily.Other, executable, app, null, entry);
        return this;
    }

    public FakeHost WithChromeProfiles()
    {
        BrowserProfiles["chrome"] = [new BrowserProfileInfo("Default", "Mayuki", null), new BrowserProfileInfo("Profile 1", "Work", "me@work.example.com")];
        return this;
    }

    /// <summary>The language of the texts while the configuration was last loaded (its load errors are in it).</summary>
    public UiLanguage? LanguageWhileLoading { get; private set; }

    /// <summary>The OS's preferred languages; null leaves the language of the texts as it is (English in tests).</summary>
    public Func<IEnumerable<string>>? SystemLanguages { get; set; }

    public RoutingService CreateService() => new(new RoutingServices(
        new Store(this), HostPlatform.MacOS, new Detector(this), new Profiles(this), new Launcher(this),
        new SystemOpener(this), new Picker(this), new Notifier(this), new Logger(this), _ => null, "/Users/me", SystemLanguages));

    private sealed class Store(FakeHost host) : IConfigStore
    {
        public ConfigLoadResult Load()
        {
            host.LanguageWhileLoading = UiLanguages.Current;
            return new(host.Config, host.Local, "/Users/me/config.json", host.ConfigExists, host.ConfigError);
        }

        public void Save(PreferisConfig config, string configPath) => host.SavedConfig = config;
    }

    private sealed class Detector(FakeHost host) : IBrowserDetector
    {
        public DetectedBrowser? TryResolve(string browserId) => host.Browsers.GetValueOrDefault(browserId);

        public IReadOnlyList<DetectedBrowser> DetectBrowsers() => host.Browsers.Values.ToList();
    }

    private sealed class Profiles(FakeHost host) : IBrowserProfileEnumerator
    {
        public IReadOnlyList<BrowserProfileInfo>? Enumerate(DetectedBrowser browser) => host.BrowserProfiles.GetValueOrDefault(browser.Id);
    }

    private sealed class Launcher(FakeHost host) : IBrowserLauncher
    {
        public void Launch(LaunchPlan plan)
        {
            if (host.FailingLaunchPaths.Contains(plan.Path))
            {
                throw new InvalidOperationException($"launch failed: {plan.Path}");
            }
            host.Launched.Add(plan);
        }
    }

    private sealed class SystemOpener(FakeHost host) : ISystemOpener
    {
        public bool IsSelfDefaultHandler(string scheme) => host.SelfIsDefaultHandler;

        public void OpenWithSystemDefault(RoutingUrl url) => host.OpenedWithSystem.Add(url);
    }

    private sealed class Picker(FakeHost host) : IPickerLauncher
    {
        public PickerResponse? Show(PickerRequest request)
        {
            host.PickerRequests.Add(request);
            return host.PickerAnswer(request);
        }
    }

    private sealed class Notifier(FakeHost host) : IUserNotifier
    {
        public void ShowError(string title, string message, RoutingUrl? url) => host.Errors.Add((title, message, url));
    }

    private sealed class Logger(FakeHost host) : IRoutingLog
    {
        public void Write(LogLevel level, string message) => host.Log.Add((level, message));
    }
}
