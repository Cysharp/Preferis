namespace Preferis.Core.Configuration;

/// <summary>Where the configuration files live on this machine. See docs/Configuration.md §1.</summary>
/// <param name="LocalConfigPath">Fixed location of <c>local.json</c>.</param>
/// <param name="DefaultConfigPath">Location of <c>config.json</c> unless <c>local.json</c> redirects it.</param>
/// <param name="LogDirectory">Local (never synced) log directory.</param>
public sealed record ConfigPaths(string LocalConfigPath, string DefaultConfigPath, string LogDirectory)
{
    private const string AppFolder = "Preferis";

    public static ConfigPaths ForWindows(string localAppData, string roamingAppData) => new(
        Path.Combine(localAppData, AppFolder, "local.json"),
        Path.Combine(roamingAppData, AppFolder, "config.json"),
        Path.Combine(localAppData, AppFolder, "Logs"));

    public static ConfigPaths ForMacOS(string homeDirectory)
    {
        var support = Path.Combine(homeDirectory, "Library", "Application Support", AppFolder);
        return new(
            Path.Combine(support, "local.json"),
            Path.Combine(support, "config.json"),
            Path.Combine(homeDirectory, "Library", "Logs", AppFolder));
    }

    public static ConfigPaths ForCurrentPlatform() => PlatformInfo.Current switch
    {
        Platform.Windows => ForWindows(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        _ => ForMacOS(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    };
}
