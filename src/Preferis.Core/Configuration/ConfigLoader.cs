using System.Text.Json;
using Preferis.Core.Hosting;
using Preferis.Core.Profiles;

namespace Preferis.Core.Configuration;

/// <summary>Result of <see cref="ConfigLoader.Load"/>. Never throws: a broken file yields the default configuration plus <see cref="Error"/>.</summary>
/// <param name="Exists">Whether a <c>config.json</c> was found at <see cref="ConfigPath"/>.</param>
/// <param name="Error">Why the files could not be read, or null.</param>
public sealed record ConfigLoadResult(PreferisConfig Config, LocalConfig Local, string ConfigPath, bool Exists, string? Error);

/// <summary>Locates, reads and writes <c>local.json</c> and <c>config.json</c>.</summary>
public sealed class ConfigLoader : IConfigStore
{
    private readonly ConfigPaths _paths;
    private readonly Platform _platform;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _homeDirectory;

    public ConfigLoader(ConfigPaths paths, Platform platform, Func<string, string?> getEnvironmentVariable, string homeDirectory)
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

    public ConfigLoadResult Load() => Load(configPathOverride: null);

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
                errors.Add($"local.json could not be read ({_paths.LocalConfigPath}): {ex.Message}");
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
                errors.Add($"config.json could not be read ({configPath}): {ex.Message}");
                config = PreferisConfig.CreateDefault();
            }
        }

        return new ConfigLoadResult(config, local, configPath, exists, errors.Count == 0 ? null : string.Join(Environment.NewLine, errors));
    }

    /// <summary>Writes <c>config.json</c> atomically (temp file + rename) so a sync client never sees a half written file.</summary>
    public void Save(PreferisConfig config, string configPath) => WriteAtomically(configPath, ConfigSerializer.Serialize(config));

    public void SaveLocal(LocalConfig local) => WriteAtomically(_paths.LocalConfigPath, ConfigSerializer.SerializeLocal(local));

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
