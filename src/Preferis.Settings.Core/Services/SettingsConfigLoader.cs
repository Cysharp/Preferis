using Preferis.Core;
using Preferis.Core.Configuration;
using Preferis.Core.Localization;
using Preferis.Platform;

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

    /// <summary>
    /// Shows the settings in the language of <c>config.json</c> (docs/Design.md §9.4). Called once before the window and its
    /// view models exist, as their drop-down lists are built once; a change shows the next time the settings open.
    /// </summary>
    public static void UseConfiguredLanguage(ConfigLoader loader)
        => UiLanguages.Current = UiLanguages.Resolve(loader.Load().Config.Settings?.Language, SystemLanguages.Preferred);
}
