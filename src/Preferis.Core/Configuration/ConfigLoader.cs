using System.Diagnostics;
using System.Text.Json;
using Preferis.Core.Hosting;
using Preferis.Core.Profiles;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Configuration;

/// <summary>Result of <see cref="ConfigLoader.Load"/>. Never throws: a broken file yields the default configuration plus <see cref="Error"/>.</summary>
/// <param name="Exists">Whether a <c>config.json</c> was found at <see cref="ConfigPath"/>.</param>
/// <param name="Error">Why the files could not be read, or null.</param>
public sealed record ConfigLoadResult(PreferisConfig Config, LocalConfig Local, string ConfigPath, bool Exists, string? Error);

/// <summary>Locates, reads and writes <c>local.json</c> and <c>config.json</c>.</summary>
public sealed class ConfigLoader : IConfigStore
{
    private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(20);

    private readonly ConfigPaths _paths;
    private readonly HostPlatform _platform;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _homeDirectory;

    public ConfigLoader(ConfigPaths paths, HostPlatform platform, Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        _paths = paths;
        _platform = platform;
        _getEnvironmentVariable = getEnvironmentVariable;
        _homeDirectory = homeDirectory;
    }

    public static ConfigLoader ForCurrentMachine() => new(
        ConfigPaths.ForCurrentPlatform(),
        PlatformInfo.Current,
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public ConfigPaths Paths => _paths;

    /// <summary>
    /// How long <see cref="Lock"/> waits for another process to release the lock. The holders read and write one small
    /// file, so it is normally free within milliseconds; waiting longer would only hold up the URL being opened or the
    /// settings window.
    /// </summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public ConfigLoadResult Load() => Load(configPathOverride: null);

    /// <summary>
    /// Takes this machine's lock on the configuration, held until the result is disposed. Whoever reads
    /// <c>config.json</c> to write it back changed (the Router remembering a choice, the settings UI saving its edits)
    /// does both under it, so that nobody writes in between what the write would then replace. Writing atomically does
    /// not prevent that: each writer replaces the whole file with what it read plus its own change.
    /// </summary>
    /// <remarks>
    /// The lock is a file nobody else may open (<see cref="FileShare.None"/>). Windows refuses other opens with a
    /// sharing violation; .NET on macOS takes <c>flock(LOCK_EX | LOCK_NB)</c>, which refuses other opens in this process
    /// as well (the macOS Router routes the URLs of one Apple Event in parallel). The OS releases it with the process,
    /// so a crash leaves no stale lock. It only orders the processes of this machine: a sync client, or Preferis on
    /// another machine, may still replace <c>config.json</c> at any time (docs/Configuration.md §1.1).
    /// </remarks>
    /// <exception cref="IOException">The lock was not released within <see cref="LockTimeout"/>.</exception>
    public IDisposable Lock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.LockPath)!);
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(_paths.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            // Held elsewhere: a sharing violation (Windows) or EWOULDBLOCK (macOS), both a plain IOException. Its
            // subclasses (a missing directory, a path too long) do not go away by waiting.
            catch (IOException ex) when (ex.GetType() == typeof(IOException))
            {
                if (waiting.Elapsed >= LockTimeout)
                {
                    throw new IOException(L("The configuration is locked by another Preferis process ({0}).", _paths.LockPath), ex);
                }
                Thread.Sleep(LockRetryInterval);
            }
        }
    }

    /// <summary>Reads <c>local.json</c> only (machine settings such as the log level). A broken or missing file yields defaults.</summary>
    public LocalConfig LoadLocal()
    {
        if (!File.Exists(_paths.LocalConfigPath))
        {
            return new LocalConfig();
        }
        try
        {
            return ConfigSerializer.DeserializeLocal(File.ReadAllText(_paths.LocalConfigPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new LocalConfig();
        }
    }

    /// <param name="configPathOverride">Explicit <c>config.json</c> path (e.g. from <c>--config</c>), bypassing <c>local.json</c> redirection.</param>
    public ConfigLoadResult Load(string? configPathOverride)
    {
        var errors = new List<string>();
        var local = new LocalConfig();
        if (File.Exists(_paths.LocalConfigPath))
        {
            try
            {
                local = ConfigSerializer.DeserializeLocal(File.ReadAllText(_paths.LocalConfigPath));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                errors.Add(L("local.json could not be read ({0}): {1}", _paths.LocalConfigPath, ex.Message));
            }
        }

        var configPath = configPathOverride
            ?? (local.ConfigPath is { Length: > 0 } redirected
                ? EnvironmentExpander.Expand(redirected, _platform, _getEnvironmentVariable, _homeDirectory)
                : _paths.DefaultConfigPath);

        var config = PreferisConfig.CreateDefault();
        var exists = File.Exists(configPath);
        if (exists)
        {
            try
            {
                config = ConfigSerializer.Deserialize(File.ReadAllText(configPath));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                errors.Add(L("config.json could not be read ({0}): {1}", configPath, ex.Message));
                config = PreferisConfig.CreateDefault();
            }
        }

        return new ConfigLoadResult(config, local, configPath, exists, errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
    }

    /// <summary>Writes <c>config.json</c> atomically (temp file + rename) so a sync client never sees a half written file.</summary>
    public void Save(PreferisConfig config, string configPath) => WriteAtomically(configPath, ConfigSerializer.Serialize(config));

    public void SaveLocal(LocalConfig local) => WriteAtomically(_paths.LocalConfigPath, ConfigSerializer.SerializeLocal(local));

    /// <summary>
    /// "Move to cloud storage": writes <paramref name="config"/> to <paramref name="newPath"/> and points <c>local.json</c>
    /// at it. Moving to the default location removes the redirection. The file at <paramref name="currentPath"/> is deleted
    /// only when it is this machine's default location: anywhere else it may be shared with other machines.
    /// </summary>
    /// <exception cref="IOException"><paramref name="newPath"/> exists and <paramref name="overwrite"/> is false, or a file operation failed.</exception>
    public void RelocateConfig(PreferisConfig config, string currentPath, string newPath, bool overwrite)
    {
        var target = Path.GetFullPath(newPath);
        var current = Path.GetFullPath(currentPath);
        var samePlace = string.Equals(target, current, StringComparison.Ordinal);
        if (!samePlace && !overwrite && File.Exists(target))
        {
            throw new IOException(L("A file already exists at {0}.", target));
        }

        // Write the new file before switching over, so that a failure leaves the old configuration in effect.
        Save(config, target);
        PointLocalConfigAt(target);
        if (!samePlace && string.Equals(current, Path.GetFullPath(_paths.DefaultConfigPath), StringComparison.Ordinal) && File.Exists(current))
        {
            File.Delete(current);
            // Two spellings of one file (a symbolic link in the path, /var vs /private/var, letter case): the delete
            // removed what was just written. Paths cannot be compared reliably across platforms, so check the result.
            if (!File.Exists(target))
            {
                Save(config, target);
            }
        }
    }

    /// <summary>Points <c>local.json</c> at a configuration that already exists (e.g. synced from another machine) without modifying it.</summary>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a Preferis configuration.</exception>
    public void UseExistingConfig(string path)
    {
        var target = Path.GetFullPath(path);
        if (!File.Exists(target))
        {
            throw new FileNotFoundException(L("There is no file at {0}.", target), target);
        }
        try
        {
            ConfigSerializer.Deserialize(File.ReadAllText(target));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(L("{0} is not a Preferis configuration: {1}", target, ex.Message), ex);
        }
        PointLocalConfigAt(target);
    }

    private void PointLocalConfigAt(string configPath)
    {
        var local = LoadLocal();
        local.ConfigPath = string.Equals(configPath, Path.GetFullPath(_paths.DefaultConfigPath), StringComparison.Ordinal) ? null : configPath;
        SaveLocal(local);
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
