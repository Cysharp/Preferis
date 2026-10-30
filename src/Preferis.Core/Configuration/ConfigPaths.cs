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

    /// <summary>
    /// Moves what Preferis kept in <c>%LocalAppData%\Preferis</c> and <c>%AppData%\Preferis</c> until 2026-10-30 into
    /// the folders of <see cref="ForWindows"/>, once, and removes the old folders. The old local folder is the one the
    /// installer owns: files left in it made Setup take Preferis for already installed, and its Repair deleted them.
    /// A file that already exists at the new place stays where it is, and so does one that cannot be moved (open in
    /// another process), for the next start to try again. Never throws: starting Preferis must not depend on this.
    /// </summary>
    public static void MoveFromLegacyWindowsFolders(string localAppData, string roamingAppData)
    {
        MoveFolder(Path.Combine(localAppData, AppFolder), Path.Combine(localAppData, Company, AppFolder));
        MoveFolder(Path.Combine(roamingAppData, AppFolder), Path.Combine(roamingAppData, Company, AppFolder));
    }

    private static void MoveFolder(string oldFolder, string newFolder)
    {
        if (!Directory.Exists(oldFolder))
        {
            return;
        }
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(oldFolder).ToList())
            {
                var name = Path.GetFileName(entry);
                var target = Path.Combine(newFolder, name);
                try
                {
                    // The lock file is empty and made on demand; a temp file of an interrupted atomic write is nothing to keep.
                    if (string.Equals(name, "config.lock", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(entry);
                    }
                    else if (!File.Exists(target) && !Directory.Exists(target))
                    {
                        Directory.CreateDirectory(newFolder);
                        if (Directory.Exists(entry))
                        {
                            Directory.Move(entry, target);
                        }
                        else
                        {
                            File.Move(entry, target);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Left for the next start.
                }
            }
            if (!Directory.EnumerateFileSystemEntries(oldFolder).Any())
            {
                Directory.Delete(oldFolder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder could not be read or removed; the files that moved are in place either way.
        }
    }

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
