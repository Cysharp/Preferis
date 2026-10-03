using System.Diagnostics;
using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

/// <summary>
/// The lock that orders reading, changing and writing <c>config.json</c> on this machine (docs/Configuration.md §1.1).
/// Each loader stands for a process of its own: what they share is the file, not the loader object.
/// </summary>
public sealed class ConfigLoaderLockTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("preferis-lock-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ConfigPaths Paths => new(Path.Combine(_root, "local", "local.json"), Path.Combine(_root, "roaming", "config.json"), Path.Combine(_root, "logs"));

    private ConfigLoader Loader(TimeSpan? lockTimeout = null)
        => new(Paths, HostPlatform.MacOS, _ => null, _root) { LockTimeout = lockTimeout ?? TimeSpan.FromSeconds(5) };

    [Fact]
    public void Nobody_else_takes_the_lock_until_it_is_released()
    {
        var other = Loader(lockTimeout: TimeSpan.FromMilliseconds(100));

        using (Loader().Lock())
        {
            var failure = Assert.Throws<IOException>(() => other.Lock());
            Assert.Contains("another Preferis process", failure.Message, StringComparison.Ordinal);
        }

        using (other.Lock())
        {
        }
    }

    [Fact]
    public void Taking_the_lock_waits_for_it_to_be_released()
    {
        var held = Loader().Lock();
        var release = new Thread(() =>
        {
            Thread.Sleep(200);
            held.Dispose();
        });
        var waited = Stopwatch.StartNew();
        release.Start();

        using (Loader().Lock())
        {
            Assert.True(waited.Elapsed >= TimeSpan.FromMilliseconds(150), $"Took the lock after {waited.Elapsed.TotalMilliseconds} ms while it was held.");
        }
        release.Join();
    }

    [Fact]
    public void The_lock_stays_with_local_json_when_config_json_is_in_cloud_storage()
    {
        var cloud = Path.Combine(_root, "cloud");
        Directory.CreateDirectory(cloud);
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.LocalConfigPath)!);
        File.WriteAllText(Path.Combine(cloud, "config.json"), Fixtures.ConfigSample);
        File.WriteAllText(Paths.LocalConfigPath, $$"""{ "configPath": "{{Path.Combine(cloud, "config.json").Replace("\\", "\\\\", StringComparison.Ordinal)}}" }""");

        using (Loader().Lock())
        {
        }

        Assert.Equal(Path.Combine(_root, "local", "config.lock"), Paths.LockPath);
        Assert.True(File.Exists(Paths.LockPath));
        Assert.Equal([Path.Combine(cloud, "config.json")], Directory.GetFiles(cloud));
    }
}
