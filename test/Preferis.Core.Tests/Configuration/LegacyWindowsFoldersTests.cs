using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

/// <summary>
/// Until 2026-10-30 the Windows files lived in <c>%LocalAppData%\Preferis</c> and <c>%AppData%\Preferis</c>; the first is
/// also the folder the Velopack installer owns (docs/Platform.md §2.1), so they moved to <c>Cysharp\Preferis</c>.
/// </summary>
public sealed class LegacyWindowsFoldersTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("preferis-legacy-").FullName;
    private readonly string _local;
    private readonly string _roaming;
    private readonly ConfigPaths _paths;

    public LegacyWindowsFoldersTests()
    {
        _local = Path.Combine(_root, "Local");
        _roaming = Path.Combine(_root, "Roaming");
        _paths = ConfigPaths.ForWindows(_local, _roaming);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string OldLocal => Path.Combine(_local, "Preferis");

    private string OldRoaming => Path.Combine(_roaming, "Preferis");

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void The_files_of_the_old_folders_move_to_the_new_ones_and_the_old_folders_go()
    {
        Write(Path.Combine(OldLocal, "local.json"), "{ \"configPath\": \"D:/sync/config.json\" }");
        Write(Path.Combine(OldLocal, "config.lock"), "");
        Write(Path.Combine(OldLocal, "Logs", "router.log"), "log");
        Write(Path.Combine(OldLocal, "Logs", "history.jsonl"), "history");
        Write(Path.Combine(OldRoaming, "config.json"), "{ \"version\": 1 }");
        Write(Path.Combine(OldRoaming, "config.old.json"), "{ \"version\": 0 }");

        ConfigPaths.MoveFromLegacyWindowsFolders(_local, _roaming);

        Assert.Equal("{ \"configPath\": \"D:/sync/config.json\" }", File.ReadAllText(_paths.LocalConfigPath));
        Assert.Equal("log", File.ReadAllText(Path.Combine(_paths.LogDirectory, "router.log")));
        Assert.Equal("history", File.ReadAllText(_paths.HistoryPath));
        Assert.Equal("{ \"version\": 1 }", File.ReadAllText(_paths.DefaultConfigPath));
        Assert.Equal("{ \"version\": 0 }", File.ReadAllText(Path.ChangeExtension(_paths.DefaultConfigPath, ".old.json")));
        // The lock file is recreated on demand; nothing of Preferis stays in the installer's folder.
        Assert.False(Directory.Exists(OldLocal));
        Assert.False(Directory.Exists(OldRoaming));
    }

    [Fact]
    public void Nothing_is_written_over_a_file_that_already_exists_in_the_new_folder()
    {
        Write(_paths.LocalConfigPath, "new");
        Write(Path.Combine(OldLocal, "local.json"), "old");
        Write(Path.Combine(OldLocal, "Logs", "router.log"), "log");

        ConfigPaths.MoveFromLegacyWindowsFolders(_local, _roaming);

        Assert.Equal("new", File.ReadAllText(_paths.LocalConfigPath));
        Assert.Equal("old", File.ReadAllText(Path.Combine(OldLocal, "local.json")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(_paths.LogDirectory, "router.log")));
        Assert.True(Directory.Exists(OldLocal));
    }

    [Fact]
    public void Without_old_folders_nothing_is_created()
    {
        ConfigPaths.MoveFromLegacyWindowsFolders(_local, _roaming);

        Assert.False(Directory.Exists(_local));
        Assert.False(Directory.Exists(_roaming));
    }

    [Fact]
    public void An_old_folder_with_nothing_but_the_lock_file_is_removed()
    {
        Write(Path.Combine(OldLocal, "config.lock"), "");

        ConfigPaths.MoveFromLegacyWindowsFolders(_local, _roaming);

        Assert.False(Directory.Exists(OldLocal));
        Assert.False(File.Exists(_paths.LocalConfigPath));
    }
}
