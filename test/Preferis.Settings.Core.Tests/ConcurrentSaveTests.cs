using System.Runtime.ExceptionServices;
using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Core.Tests;

/// <summary>
/// The settings writing <c>config.json</c> while the Router remembers a picker choice in it (docs/Design.md §9.1, §9.2).
/// Both read, compare or change, and write under this machine's lock, so neither replaces the other's change unseen.
/// </summary>
public sealed class ConcurrentSaveTests : IDisposable
{
    private const string MicrosoftLogin = "https://login.microsoftonline.com/common";

    private readonly SettingsHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_save_while_the_router_remembers_waits_for_it_and_holds_the_edits()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Mine";
        Background? save = null;
        // The pause after the edit ends right after the Router has read the file to add its rule to. Unlocked, the save
        // finds the file as it was shown and writes the edit, and the Router's write then replaces it.
        var router = new FakeRouter(NewLoader(), _harness.Platform, afterLoad: count =>
        {
            if (count == 2)
            {
                save = new Background(_harness.Saves.RunPending);
                save.Wait(TimeSpan.FromMilliseconds(300));
            }
        });

        router.Open(MicrosoftLogin, "edge");
        save!.Finish();

        Assert.True(settings.HasExternalChanges);
        Assert.True(settings.IsDirty);
        Assert.Equal("Mine", settings.Profiles.Profiles[0].Name);
        var saved = _harness.SavedConfig();
        Assert.Equal("Microsoft Edge", saved.Profiles[0].Name);
        Assert.Contains(saved.Rules, r => r.Id.StartsWith("remember-", StringComparison.Ordinal));
    }

    [Fact]
    public void Moving_the_file_while_the_router_remembers_takes_the_remembered_rule_along()
    {
        var settings = _harness.WithSample().Create();
        var cloud = Path.Combine(Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName, "config.json");
        Background? move = null;
        // Unlocked, the move copies the file without the rule and deletes it, and the Router then writes it back.
        var router = new FakeRouter(NewLoader(), _harness.Platform, afterLoad: count =>
        {
            if (count == 2)
            {
                move = new Background(() => settings.Relocate(cloud, overwrite: false));
                move.Wait(TimeSpan.FromMilliseconds(300));
            }
        });

        router.Open(MicrosoftLogin, "edge");
        move!.Finish();

        Assert.Equal(cloud, settings.ConfigPath);
        Assert.Contains(_harness.SavedConfig().Rules, r => r.Id.StartsWith("remember-", StringComparison.Ordinal));
        Assert.Contains(settings.Rules.Rules, r => r.Id.StartsWith("remember-", StringComparison.Ordinal));
        Assert.False(File.Exists(_harness.Paths.DefaultConfigPath));
    }

    [Fact]
    public void Moving_edits_while_the_router_remembers_stops_at_the_conflict()
    {
        var settings = _harness.WithSample().Create();
        settings.Profiles.Profiles[0].Name = "Mine";
        var cloud = Path.Combine(Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName, "config.json");
        Background? move = null;
        var router = new FakeRouter(NewLoader(), _harness.Platform, afterLoad: count =>
        {
            if (count == 2)
            {
                move = new Background(() => settings.Relocate(cloud, overwrite: false));
                move.Wait(TimeSpan.FromMilliseconds(300));
            }
        });

        router.Open(MicrosoftLogin, "edge");

        var failure = Assert.Throws<IOException>(move!.Finish);
        Assert.Contains("changed on disk", failure.Message, StringComparison.Ordinal);
        Assert.True(settings.HasExternalChanges);
        Assert.Equal("Mine", settings.Profiles.Profiles[0].Name);
        Assert.Equal(_harness.Paths.DefaultConfigPath, settings.ConfigPath);
        Assert.False(File.Exists(cloud));
        Assert.Contains(_harness.SavedConfig().Rules, r => r.Id.StartsWith("remember-", StringComparison.Ordinal));
    }

    [Fact]
    public void Moving_stops_when_the_file_can_no_longer_be_read()
    {
        var settings = _harness.WithSample().Create();
        var cloud = Path.Combine(Directory.CreateDirectory(Path.Combine(_harness.Root, "Cloud")).FullName, "config.json");
        // e.g. a sync client brought in a broken copy after the page checked, before the move took the lock: read again,
        // it is an unreadable file shown as the default configuration, which must not replace it anywhere.
        File.WriteAllText(_harness.Paths.DefaultConfigPath, "{ broken");

        var failure = Assert.Throws<IOException>(() => settings.Relocate(cloud, overwrite: false));

        Assert.Contains("cannot be read", failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(cloud));
        Assert.Equal("{ broken", File.ReadAllText(_harness.Paths.DefaultConfigPath));
        Assert.Equal(_harness.Paths.DefaultConfigPath, settings.ConfigPath);
    }

    [Fact]
    public void Saving_only_local_json_does_not_wait_for_the_lock()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson);
        var settings = new SettingsViewModel(NewLoader(lockTimeout: TimeSpan.FromMilliseconds(100)), _harness.Platform, _harness.Dialogs, _harness.Saves);
        settings.Profiles.Profiles.Single(p => p.Id == "edge").UsesLocalTarget = true;

        // The lock orders writers of config.json; this computer's own file is no part of that.
        using (NewLoader().Lock())
        {
            _harness.Saves.RunPending();
        }

        Assert.False(settings.IsDirty);
        Assert.Null(settings.StatusMessage);
        Assert.True(_harness.Loader.LoadLocal().ProfileTargets?.ContainsKey("edge"));
        Assert.Equal(SettingsHarness.SampleJson, File.ReadAllText(_harness.Paths.DefaultConfigPath));
    }

    [Fact]
    public async Task A_save_that_cannot_take_the_lock_keeps_the_edits_and_says_why()
    {
        File.WriteAllText(_harness.Paths.DefaultConfigPath, SettingsHarness.SampleJson);
        var settings = new SettingsViewModel(NewLoader(lockTimeout: TimeSpan.FromMilliseconds(100)), _harness.Platform, _harness.Dialogs, _harness.Saves);
        settings.Profiles.Profiles[0].Name = "Mine";

        // e.g. a Router that stopped responding while remembering.
        using (NewLoader().Lock())
        {
            _harness.Saves.RunPending();
        }

        Assert.True(settings.IsDirty);
        Assert.False(settings.HasExternalChanges);
        Assert.StartsWith("Could not save: The configuration is locked by another Preferis process", settings.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(SettingsHarness.SampleJson, File.ReadAllText(_harness.Paths.DefaultConfigPath));

        // Once the lock is free, closing the window saves them without asking.
        Assert.True(await settings.CanCloseAsync());
        Assert.Empty(_harness.Dialogs.Asked);
        Assert.Equal("Mine", _harness.SavedConfig().Profiles[0].Name);
        Assert.Null(settings.StatusMessage);
    }

    // Another process's loader over the same files: the Router's, or one that holds the lock meanwhile.
    private ConfigLoader NewLoader(TimeSpan? lockTimeout = null)
        => new(_harness.Paths, HostPlatform.MacOS, _ => null, _harness.Root) { LockTimeout = lockTimeout ?? TimeSpan.FromSeconds(5) };

    /// <summary>Runs an action on a thread of its own; its exception, if any, is thrown by <see cref="Finish"/>.</summary>
    private sealed class Background
    {
        private readonly Thread _thread;
        private Exception? _failure;

        public Background(Action action)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _failure = ex;
                }
            });
            _thread.Start();
        }

        /// <summary>Gives the action <paramref name="timeout"/> to finish, as it would unless something holds it up.</summary>
        public void Wait(TimeSpan timeout) => _thread.Join(timeout);

        public void Finish()
        {
            _thread.Join();
            if (_failure is not null)
            {
                ExceptionDispatchInfo.Throw(_failure);
            }
        }
    }

    /// <summary>
    /// The Router opening a URL whose picker chooses a profile and asks to remember it: real routing over real files, with
    /// the browsers and the picker faked. <c>afterLoad</c> gets the number of reads of the configuration so far after each
    /// one; the second is the one the rule is added to.
    /// </summary>
    private sealed class FakeRouter(ConfigLoader loader, FakeSettingsPlatform platform, Action<int> afterLoad)
        : IConfigStore, IBrowserDetector, IBrowserProfileEnumerator, IBrowserLauncher, ISystemOpener, IPickerLauncher, IUserNotifier, IRoutingLog
    {
        private int _loads;
        private string? _choice;

        public HandleResult Open(string url, string choice)
        {
            _choice = choice;
            var services = new RoutingServices(this, platform.Platform, this, this, this, this, this, this, this, _ => null, platform.HomeDirectory);
            return new RoutingService(services).Handle(RoutingUrl.Parse(url));
        }

        public ConfigLoadResult Load()
        {
            var result = loader.Load();
            afterLoad(++_loads);
            return result;
        }

        public void Save(PreferisConfig config, string configPath) => loader.Save(config, configPath);

        public IDisposable Lock() => loader.Lock();

        public DetectedBrowser? TryResolve(string browserId) => platform.BrowserLocator.TryResolve(browserId);

        public IReadOnlyList<DetectedBrowser> DetectBrowsers() => [.. platform.Browsers.Select(b => b.Browser)];

        public IReadOnlyList<BrowserProfileInfo>? Enumerate(DetectedBrowser browser) => platform.BrowserProfiles.Enumerate(browser);

        public void Launch(LaunchPlan plan)
        {
        }

        public bool IsSelfDefaultHandler(string scheme) => false;

        public void OpenWithSystemDefault(RoutingUrl url)
        {
        }

        public PickerResponse? Show(PickerRequest request) => new() { Result = PickerResult.Selected, ProfileId = _choice, Remember = true };

        public void ShowError(string title, string message, RoutingUrl? url)
        {
        }

        public void Write(LogLevel level, string message)
        {
        }
    }
}
