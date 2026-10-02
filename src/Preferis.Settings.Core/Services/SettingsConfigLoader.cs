using Preferis.Core;
using Preferis.Core.Configuration;

namespace Preferis.Settings.Services;

/// <summary>Where the settings app reads and writes the configuration.</summary>
public static class SettingsConfigLoader
{
    /// <summary>
    /// The configuration of this machine. Snapshots (developer aid) take <c>config.json</c> and <c>local.json</c> from the
    /// folder in <c>PREFERIS_SETTINGS_SNAPSHOT_CONFIG</c> when it is set, so that pages can be captured with a prepared
    /// configuration without touching the real one.
    /// </summary>
    public static ConfigLoader Create()
    {
        if (Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT") is { Length: > 0 }
            && Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT_CONFIG") is { Length: > 0 } directory)
        {
            var folder = Path.GetFullPath(directory);
            var paths = new ConfigPaths(Path.Combine(folder, "local.json"), Path.Combine(folder, "config.json"), Path.Combine(folder, "logs"));
            return new ConfigLoader(paths, PlatformInfo.Current, Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        return ConfigLoader.ForCurrentMachine();
    }
}
