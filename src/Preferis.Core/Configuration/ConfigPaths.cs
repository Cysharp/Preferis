namespace Preferis.Core.Configuration;

/// <summary>Where the configuration files live on this machine. See docs/Configuration.md §1.</summary>
/// <param name="LocalConfigPath">Fixed location of <c>local.json</c>.</param>
/// <param name="DefaultConfigPath">Location of <c>config.json</c> unless <c>local.json</c> redirects it.</param>
/// <param name="LogDirectory">Local (never synced) log directory.</param>
public sealed record ConfigPaths(string LocalConfigPath, string DefaultConfigPath, string LogDirectory)
{
    private const string AppFolder = "Preferis";

    // Windows keeps application data as Company\Product. %LocalAppData%\Preferis alone is where the installer puts the
    // program (docs/Platform.md §2.1), and the installer owns that folder entirely.
    private const string Company = "Cysharp";

    /// <summary>The opt-in routing history (contains URLs, so it stays in the local log directory).</summary>
    public string HistoryPath => Path.Combine(LogDirectory, "history.jsonl");

    /// <summary>
    /// The lock file of <see cref="ConfigLoader.Lock"/>. It sits with <c>local.json</c>, which is never synced, rather
    /// than with <c>config.json</c>: in cloud storage the lock file would be uploaded and handed to other machines, to
    /// which a lock on this one means nothing.
    /// </summary>
    public string LockPath => Path.Combine(Path.GetDirectoryName(LocalConfigPath)!, "config.lock");

    public static ConfigPaths ForWindows(string localAppData, string roamingAppData) => new(
        Path.Combine(localAppData, Company, AppFolder, "local.json"),
        Path.Combine(roamingAppData, Company, AppFolder, "config.json"),
        Path.Combine(localAppData, Company, AppFolder, "Logs"));

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
        HostPlatform.Windows => ForWindows(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        _ => ForMacOS(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
    };
}
