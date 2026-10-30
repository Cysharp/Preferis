using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

internal sealed class FakeSettingsPlatform : ISettingsPlatform
{
    public List<DetectedBrowserInfo> Browsers { get; } = [];

    public RegistrationStatus Status { get; set; } = new(IsRegistered: true, IsDefault: false, CurrentDefault: "/Applications/Safari.app");

    public RegistrationResult RegisterResult { get; set; } = new(true, "Registered.");

    public bool CanRegister { get; set; } = true;

    public int RegisterCalls { get; private set; }

    public int SystemSettingsOpened { get; private set; }

    public bool CanOpenUrls { get; set; } = true;

    public List<string> OpenedUrls { get; } = [];

    public RouterRunResult OpenResult { get; set; } = new(0, ["[Info] Launched: Launched 'edge'. (12.3 ms)"]);

    public List<string> Revealed { get; } = [];

    public HostPlatform Platform { get; set; } = HostPlatform.MacOS;

    /// <summary>Null, as on macOS and for the portable zip, unless a test installs one.</summary>
    public IAppUpdater? Updater { get; set; }

    public IBrowserLocator BrowserLocator => new Locator(this);

    public IBrowserProfileEnumerator BrowserProfiles => new Enumerator(this);

    public Func<string, string?> GetEnvironmentVariable => _ => null;

    public string HomeDirectory => "/Users/me";

    public IReadOnlyList<DetectedBrowserInfo> DetectBrowsers() => Browsers.ToList();

    public RegistrationStatus GetRegistrationStatus() => Status;

    public Task<RegistrationResult> RegisterAsync()
    {
        RegisterCalls++;
        return Task.FromResult(RegisterResult);
    }

    public void OpenDefaultBrowserSettings() => SystemSettingsOpened++;

    /// <summary>What the Router does meanwhile, e.g. save a remembered rule.</summary>
    public Action<string>? OnOpen { get; set; }

    public Task<RouterRunResult> OpenUrlAsync(string url)
    {
        OpenedUrls.Add(url);
        OnOpen?.Invoke(url);
        return Task.FromResult(OpenResult);
    }

    public void Reveal(string path) => Revealed.Add(path);

    public FakeSettingsPlatform WithBrowser(string id, IReadOnlyList<BrowserProfileInfo>? profiles = null)
    {
        var entry = BrowserCatalog.Find(id);
        var name = entry?.DisplayName ?? id;
        var app = $"/Applications/{name}.app";
        var userData = entry?.MacUserDataDir is { } dir ? $"/Users/me/Library/Application Support/{dir}" : null;
        Browsers.Add(new DetectedBrowserInfo(
            new DetectedBrowser(id, name, entry?.Family ?? BrowserFamily.Other, $"{app}/Contents/MacOS/{name}", app, userData, entry),
            profiles,
            IconPng: null));
        return this;
    }

    /// <summary>This machine runs Windows, with Edge (one profile) and Firefox; Safari does not exist there.</summary>
    public FakeSettingsPlatform OnWindows()
    {
        Platform = HostPlatform.Windows;
        Browsers.Clear();
        AddWindowsBrowser("edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", [new("Default", "Personal", "me@example.com")]);
        AddWindowsBrowser("firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe", [new("Profiles/abc.default-release", "default-release", null)]);
        return this;
    }

    private void AddWindowsBrowser(string id, string executable, IReadOnlyList<BrowserProfileInfo>? profiles)
    {
        var entry = BrowserCatalog.Find(id)!;
        Browsers.Add(new DetectedBrowserInfo(
            new DetectedBrowser(id, entry.DisplayName, entry.Family, executable, null, $@"C:\Users\me\AppData\Local\{entry.WindowsUserDataDir}", entry),
            profiles,
            IconPng: null));
    }

    /// <summary>Chrome (two profiles), Edge (one), Safari (none) and Firefox (two named profiles).</summary>
    public FakeSettingsPlatform WithTypicalBrowsers() => this
        .WithBrowser("chrome", [new("Default", "Mayuki", null), new("Profile 1", "Work", "me@work.example.com")])
        .WithBrowser("edge", [new("Default", "Personal", "me@example.com")])
        .WithBrowser("safari")
        .WithBrowser("firefox", [new("Profiles/abc.default-release", "default-release", null), new("Profiles/def.work", "Work", null)]);

    private sealed class Locator(FakeSettingsPlatform platform) : IBrowserLocator
    {
        public DetectedBrowser? TryResolve(string browserId) => platform.Browsers.FirstOrDefault(b => b.Browser.Id == browserId)?.Browser;
    }

    private sealed class Enumerator(FakeSettingsPlatform platform) : IBrowserProfileEnumerator
    {
        public IReadOnlyList<BrowserProfileInfo>? Enumerate(DetectedBrowser browser) => platform.Browsers.FirstOrDefault(b => b.Browser.Id == browser.Id)?.Profiles;
    }
}

/// <summary>The installer's updates: what the next check finds, and how a download goes.</summary>
internal sealed class FakeAppUpdater : IAppUpdater
{
    public string CurrentVersion { get; set; } = "1.0.0";

    /// <summary>What checking finds; null is "up to date".</summary>
    public AppUpdate? Available { get; set; }

    public Exception? CheckError { get; set; }

    public Exception? DownloadError { get; set; }

    /// <summary>Holds the check until the test completes it, for what is shown meanwhile.</summary>
    public TaskCompletionSource? CheckGate { get; set; }

    /// <summary>The percentages a download reports before it completes.</summary>
    public int[] DownloadSteps { get; set; } = [];

    public int Checks { get; private set; }

    public List<string> Downloaded { get; } = [];

    /// <summary>The version handed to the installer, or null while Preferis keeps running.</summary>
    public string? Applied { get; private set; }

    public async Task<AppUpdate?> CheckAsync()
    {
        Checks++;
        if (CheckGate is { } gate)
        {
            await gate.Task;
        }
        if (CheckError is { } error)
        {
            throw error;
        }
        return Available;
    }

    public Task DownloadAsync(AppUpdate update, Action<int> progress)
    {
        Downloaded.Add(update.Version);
        foreach (var step in DownloadSteps)
        {
            progress(step);
        }
        return DownloadError is { } error ? Task.FromException(error) : Task.CompletedTask;
    }

    public void ApplyAndRestart(AppUpdate update) => Applied = update.Version;
}

internal sealed class FakeDialogs : IDialogService
{
    public Queue<string?> Folders { get; } = new();

    public Queue<string?> Files { get; } = new();

    /// <summary>Answers to <see cref="AskAsync"/>, by button label. Unanswered questions are dismissed (-1).</summary>
    public Queue<string> Answers { get; } = new();

    /// <summary>A deferred answer to the next question, for changes made while a dialog is open.</summary>
    public Task<int>? NextAnswer { get; set; }

    public List<(string Title, string Message, IReadOnlyList<string> Buttons)> Asked { get; } = [];

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folders.Count > 0 ? Folders.Dequeue() : null);

    public Task<string?> PickFileAsync(string title) => Task.FromResult(Files.Count > 0 ? Files.Dequeue() : null);

    public Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons)
    {
        Asked.Add((title, message, buttons));
        if (NextAnswer is { } deferred)
        {
            NextAnswer = null;
            return deferred;
        }
        if (Answers.Count == 0)
        {
            return Task.FromResult(-1);
        }
        var answer = Answers.Dequeue();
        var index = buttons.ToList().IndexOf(answer);
        Assert.True(index >= 0, $"No button '{answer}' in [{string.Join(", ", buttons)}]");
        return Task.FromResult(index);
    }
}

/// <summary>Holds the save the view model asks for until the test runs it, as the UI's timer would after a pause.</summary>
internal sealed class FakeSaveScheduler : ISaveScheduler
{
    private Func<Task>? _pending;

    public bool HasPending => _pending is not null;

    public void Schedule(Func<Task> save) => _pending = save;

    /// <summary>The pause is over: runs the save that is waiting, if any.</summary>
    public async Task RunPendingAsync()
    {
        var save = _pending;
        _pending = null;
        if (save is not null)
        {
            await save();
        }
    }
}

/// <summary>A settings session over real files in a temporary directory.</summary>
internal sealed class SettingsHarness : IDisposable
{
    public SettingsHarness()
    {
        Root = Directory.CreateTempSubdirectory("preferis-settings-").FullName;
        Paths = new ConfigPaths(Path.Combine(Root, "local.json"), Path.Combine(Root, "config.json"), Path.Combine(Root, "logs"));
        Loader = new ConfigLoader(Paths, HostPlatform.MacOS, _ => null, Root);
    }

    public string Root { get; }

    public ConfigPaths Paths { get; }

    public ConfigLoader Loader { get; }

    public FakeSettingsPlatform Platform { get; } = new FakeSettingsPlatform().WithTypicalBrowsers();

    public FakeDialogs Dialogs { get; } = new();

    public FakeSaveScheduler Saves { get; } = new();

    public static string SampleJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "config.sample.json"));

    public SettingsHarness WithSample()
    {
        File.WriteAllText(Paths.DefaultConfigPath, SampleJson);
        return this;
    }

    public SettingsViewModel Create() => new(Loader, Platform, Dialogs, Saves);

    /// <summary>A session whose validation looks the pattern errors up in <paramref name="patterns"/>.</summary>
    public SettingsViewModel Create(PatternErrorCache patterns) => new(Loader, Platform, Dialogs, Saves, patterns);

    public PreferisConfig SavedConfig() => ConfigSerializer.Deserialize(File.ReadAllText(Loader.Load().ConfigPath));

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
