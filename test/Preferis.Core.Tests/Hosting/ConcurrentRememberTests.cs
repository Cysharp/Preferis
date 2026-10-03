using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Picker;
using Preferis.Core.Routing;

namespace Preferis.Core.Tests.Hosting;

/// <summary>
/// Remembering picker choices while something else writes <c>config.json</c> too, over real files in a temporary directory
/// (docs/Design.md §9.2). Each Router gets a loader of its own, as each is a process of its own.
/// </summary>
public sealed class ConcurrentRememberTests : IDisposable
{
    private const string Chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
    private const string Edge = "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";

    // Every URL shows the picker: the catch-all offers both profiles.
    private const string TwoProfiles = """
        {
          "version": 1,
          "profiles": [
            { "id": "a", "name": "A", "browser": "chrome" },
            { "id": "b", "name": "B", "browser": "edge" }
          ],
          "rules": [],
          "catchAll": { "actions": [ { "type": "open", "profiles": ["a", "b"] } ] }
        }
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("preferis-remember-").FullName;
    private readonly ConfigPaths _paths;

    public ConcurrentRememberTests()
    {
        // config.json in a folder of its own, as when it is in cloud storage.
        _paths = new ConfigPaths(Path.Combine(_root, "local", "local.json"), Path.Combine(_root, "synced", "config.json"), Path.Combine(_root, "logs"));
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.DefaultConfigPath)!);
        File.WriteAllText(_paths.DefaultConfigPath, TwoProfiles);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Two_routers_remembering_at_once_keep_both_rules()
    {
        // After reading the file to add its rule to (its second load), each Router waits for the other to have read it
        // too. Unlocked, both then write what they read and the second write drops the first rule. Locked, the other
        // cannot read before this one has written, and the wait ends by timing out.
        using var bothRead = new CountdownEvent(2);
        void AfterLoad(int count)
        {
            if (count == 2)
            {
                bothRead.Signal();
                bothRead.Wait(TimeSpan.FromMilliseconds(500));
            }
        }
        var one = Router(new HookedStore(NewLoader(), AfterLoad));
        var two = Router(new HookedStore(NewLoader(), AfterLoad));

        RunTogether(() => Handle(one, "https://one.example/"), () => Handle(two, "https://two.example/"));

        Assert.Equal(["one.example", "two.example"], RememberedHosts().Order());
        Assert.Single(one.Launched);
        Assert.Single(two.Launched);
    }

    [Fact]
    public void Remembering_waits_for_a_save_in_progress_and_adds_to_what_it_saved()
    {
        var router = Router(NewLoader());
        // Another writer (the settings UI) has compared the file with what it showed, under the lock, and writes its
        // edit a little later. Unlocked, the Router would write its rule in between, and the edit would then replace it.
        var other = NewLoader();
        var exclusive = other.Lock();
        var edited = other.Load().Config;
        edited.Profiles[1].Name = "Edited meanwhile";
        var save = new Thread(() =>
        {
            Thread.Sleep(300);
            other.Save(edited, _paths.DefaultConfigPath);
            exclusive.Dispose();
        });
        save.Start();

        Handle(router, "https://one.example/");
        save.Join();

        Assert.Equal("Edited meanwhile", ConfigSerializer.Deserialize(File.ReadAllText(_paths.DefaultConfigPath)).Profiles[1].Name);
        Assert.Equal(["one.example"], RememberedHosts());
    }

    [Fact]
    public void A_router_that_cannot_take_the_lock_opens_the_url_without_remembering()
    {
        var router = Router(NewLoader(lockTimeout: TimeSpan.FromMilliseconds(100)));
        var before = File.ReadAllText(_paths.DefaultConfigPath);

        HandleResult result;
        // e.g. another Router that stopped responding while remembering.
        using (NewLoader().Lock())
        {
            result = Handle(router, "https://one.example/");
        }

        Assert.Equal(HandleOutcome.Launched, result.Outcome);
        Assert.Equal("a", result.ProfileId);
        Assert.Equal(before, File.ReadAllText(_paths.DefaultConfigPath));
        Assert.Contains(router.Log, l => l.Level == LogLevel.Error && l.Message.Contains("one.example", StringComparison.Ordinal));
    }

    private ConfigLoader NewLoader(TimeSpan? lockTimeout = null)
        => new(_paths, HostPlatform.MacOS, _ => null, _root) { LockTimeout = lockTimeout ?? TimeSpan.FromSeconds(5) };

    /// <summary>A Router whose picker chooses profile <c>a</c> and asks to remember it.</summary>
    private static FakeHost Router(IConfigStore store)
    {
        var host = new FakeHost()
            .WithBrowser("chrome", Chrome, "/Applications/Google Chrome.app")
            .WithBrowser("edge", Edge, "/Applications/Microsoft Edge.app");
        host.ConfigStore = store;
        host.PickerAnswer = _ => new PickerResponse { Result = PickerResult.Selected, ProfileId = "a", Remember = true };
        return host;
    }

    private static HandleResult Handle(FakeHost host, string url) => host.CreateService().Handle(RoutingUrl.Parse(url));

    private IEnumerable<string> RememberedHosts() => ConfigSerializer.Deserialize(File.ReadAllText(_paths.DefaultConfigPath)).Rules
        .Where(r => r.Id.StartsWith("remember-", StringComparison.Ordinal))
        .Select(r => Assert.IsType<HostCondition>(r.Condition).Pattern);

    /// <summary>Runs each action on a thread of its own (not the thread pool, which may run them one after the other).</summary>
    private static void RunTogether(params Action[] actions)
    {
        var failures = new List<Exception>();
        var threads = actions.Select(action => new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>The real store, calling <paramref name="afterLoad"/> with the number of loads so far after each one.</summary>
    private sealed class HookedStore(ConfigLoader inner, Action<int> afterLoad) : IConfigStore
    {
        private int _loads;

        public ConfigLoadResult Load()
        {
            var result = inner.Load();
            afterLoad(Interlocked.Increment(ref _loads));
            return result;
        }

        public void Save(PreferisConfig config, string configPath) => inner.Save(config, configPath);

        public IDisposable Lock() => inner.Lock();
    }
}
