using System.Text;
using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public sealed class ConfigBackupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("preferis-backup-").FullName;
    private readonly ManualTimeProvider _time = new();

    private ConfigPaths Paths => new(Path.Combine(_root, "local.json"), Path.Combine(_root, "config.json"), Path.Combine(_root, "logs"));
    private string BackupPath => Path.Combine(_root, "config.old.json");
    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private ConfigLoader Loader() => new(Paths, HostPlatform.MacOS, _ => null, _root, _time);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_first_save_has_no_previous_file_to_back_up(bool hasBackup)
    {
        if (hasBackup)
        {
            File.WriteAllText(BackupPath, "existing backup");
            File.SetLastWriteTimeUtc(BackupPath, Now.AddDays(-2));
        }

        Loader().Save(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath);

        Assert.True(File.Exists(Paths.DefaultConfigPath));
        Assert.Equal(hasBackup, File.Exists(BackupPath));
        if (hasBackup)
        {
            Assert.Equal("existing backup", File.ReadAllText(BackupPath));
            Assert.Equal(Now.AddDays(-2), File.GetLastWriteTimeUtc(BackupPath));
        }
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"unknown\": \"保存前\" }\r\n")]
    [InlineData("{ broken JSON\r\n")]
    public void A_missing_backup_preserves_the_previous_bytes_and_records_the_backup_time(string previous)
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(previous)).ToArray();
        File.WriteAllBytes(Paths.DefaultConfigPath, bytes);
        File.SetLastWriteTimeUtc(Paths.DefaultConfigPath, Now.AddDays(-7));
        var config = PreferisConfig.CreateDefault();
        config.DefaultProfile = "edge";

        Loader().Save(config, Paths.DefaultConfigPath);

        Assert.Equal(bytes, File.ReadAllBytes(BackupPath));
        Assert.Equal(Now, File.GetLastWriteTimeUtc(BackupPath));
        Assert.Equal("edge", Loader().Load().Config.DefaultProfile);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Theory]
    [InlineData(-3600, false)]
    [InlineData(0, false)]
    [InlineData(86399, false)]
    [InlineData(86400, true)]
    [InlineData(172800, true)]
    public void An_existing_backup_is_updated_only_after_a_full_day(int ageSeconds, bool shouldUpdate)
    {
        File.WriteAllText(Paths.DefaultConfigPath, "previous configuration");
        File.WriteAllText(BackupPath, "older backup");
        var backedUpAt = Now.AddSeconds(-ageSeconds);
        File.SetLastWriteTimeUtc(BackupPath, backedUpAt);

        Loader().Save(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath);

        Assert.Equal(shouldUpdate ? "previous configuration" : "older backup", File.ReadAllText(BackupPath));
        Assert.Equal(shouldUpdate ? Now : backedUpAt, File.GetLastWriteTimeUtc(BackupPath));
        Assert.Equal(ConfigSerializer.Serialize(PreferisConfig.CreateDefault()), File.ReadAllText(Paths.DefaultConfigPath));
    }

    [Fact]
    public void Repeated_saves_across_loader_instances_keep_each_backup_for_a_day()
    {
        File.WriteAllText(Paths.DefaultConfigPath, "original configuration");
        var originalTime = Now;
        SaveWithProfile("first");

        _time.Advance(TimeSpan.FromHours(23));
        SaveWithProfile("second");
        Assert.Equal("original configuration", File.ReadAllText(BackupPath));
        Assert.Equal(originalTime, File.GetLastWriteTimeUtc(BackupPath));

        _time.Advance(TimeSpan.FromHours(1));
        SaveWithProfile("third");
        Assert.Equal("second", ConfigSerializer.Deserialize(File.ReadAllText(BackupPath)).DefaultProfile);
        Assert.Equal(Now, File.GetLastWriteTimeUtc(BackupPath));

        _time.Advance(TimeSpan.FromSeconds(1));
        SaveWithProfile("fourth");
        Assert.Equal("second", ConfigSerializer.Deserialize(File.ReadAllText(BackupPath)).DefaultProfile);
        Assert.Equal(originalTime.AddDays(1), File.GetLastWriteTimeUtc(BackupPath));
        Assert.Equal("fourth", Loader().Load().Config.DefaultProfile);
    }

    [Theory]
    [InlineData("config.json", "config.old.json")]
    [InlineData("shared.json", "shared.old.json")]
    public void A_redirected_configuration_is_backed_up_beside_the_file_being_saved(string filename, string backupFilename)
    {
        var cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        var target = Path.Combine(cloud, filename);
        File.WriteAllText(target, Fixtures.ConfigSample);
        var loader = Loader();
        loader.SaveLocal(new LocalConfig { ConfigPath = target });
        var loaded = loader.Load();

        loader.Save(PreferisConfig.CreateDefault(), loaded.ConfigPath);

        Assert.Equal(Fixtures.ConfigSample, File.ReadAllText(Path.Combine(cloud, backupFilename)));
        Assert.False(File.Exists(BackupPath));
    }

    [Fact]
    public void Saving_local_settings_does_not_create_a_backup()
    {
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);
        File.WriteAllText(Paths.LocalConfigPath, "{}");

        Loader().SaveLocal(new LocalConfig { ConfigPath = "cloud/config.json" });

        Assert.False(File.Exists(BackupPath));
        Assert.False(File.Exists(Path.Combine(_root, "local.old.json")));
        Assert.Equal(Fixtures.ConfigSample, File.ReadAllText(Paths.DefaultConfigPath));
        Assert.Equal("cloud/config.json", Loader().LoadLocal().ConfigPath);
    }

    [Fact]
    public void Relocating_over_an_existing_file_backs_up_the_destination_before_replacing_it()
    {
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);
        var cloud = Directory.CreateDirectory(Path.Combine(_root, "cloud")).FullName;
        var target = Path.Combine(cloud, "config.json");
        File.WriteAllText(target, "destination configuration");

        Loader().RelocateConfig(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath, target, overwrite: true);

        Assert.Equal("destination configuration", File.ReadAllText(Path.Combine(cloud, "config.old.json")));
        Assert.Equal(target, Loader().Load().ConfigPath);
    }

    [Fact]
    public void A_failed_backup_leaves_the_configuration_untouched_and_cleans_up_temporary_files()
    {
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);
        Directory.CreateDirectory(BackupPath);

        var error = Record.Exception(() => Loader().Save(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath));

        Assert.True(error is IOException or UnauthorizedAccessException, $"Expected a file access error, got {error}");
        Assert.Equal(Fixtures.ConfigSample, File.ReadAllText(Paths.DefaultConfigPath));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    private void SaveWithProfile(string profile)
    {
        var config = PreferisConfig.CreateDefault();
        config.DefaultProfile = profile;
        Loader().Save(config, Paths.DefaultConfigPath);
    }
}
