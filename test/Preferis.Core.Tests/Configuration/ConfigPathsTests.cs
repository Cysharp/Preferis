using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public class ConfigPathsTests
{
    [Fact]
    public void Windows_uses_local_and_roaming_app_data()
    {
        // Path.Combine is used for the expectations because the separator depends on the OS running the tests.
        var local = @"C:\Users\me\AppData\Local";
        var roaming = @"C:\Users\me\AppData\Roaming";

        var paths = ConfigPaths.ForWindows(local, roaming);

        Assert.Equal(Path.Combine(local, "Preferis", "local.json"), paths.LocalConfigPath);
        Assert.Equal(Path.Combine(roaming, "Preferis", "config.json"), paths.DefaultConfigPath);
        Assert.Equal(Path.Combine(local, "Preferis", "Logs"), paths.LogDirectory);
    }

    [Fact]
    public void MacOS_uses_application_support_and_logs()
    {
        var paths = ConfigPaths.ForMacOS(homeDirectory: "/Users/me");

        // Separators as on macOS, whichever OS runs the tests.
        Assert.Equal("/Users/me/Library/Application Support/Preferis/local.json", Unix(paths.LocalConfigPath));
        Assert.Equal("/Users/me/Library/Application Support/Preferis/config.json", Unix(paths.DefaultConfigPath));
        Assert.Equal("/Users/me/Library/Logs/Preferis", Unix(paths.LogDirectory));
        Assert.Equal("/Users/me/Library/Logs/Preferis/history.jsonl", Unix(paths.HistoryPath));

        static string Unix(string path) => path.Replace('\\', '/');
    }
}

public class ConfigLoaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("preferis-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ConfigPaths Paths => new(Path.Combine(_root, "local.json"), Path.Combine(_root, "config.json"), Path.Combine(_root, "logs"));

    private ConfigLoader Loader(HostPlatform platform = HostPlatform.MacOS) => new(Paths, platform, _ => null, _root);

    [Fact]
    public void Missing_files_yield_the_default_config_without_error()
    {
        var result = Loader().Load();

        Assert.Null(result.Error);
        Assert.Equal(Paths.DefaultConfigPath, result.ConfigPath);
        Assert.Empty(result.Config.Profiles);
        Assert.False(result.Exists);
    }

    [Fact]
    public void Reads_config_from_the_default_location()
    {
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);

        var result = Loader().Load();

        Assert.Null(result.Error);
        Assert.True(result.Exists);
        Assert.Equal(6, result.Config.Profiles.Count);
    }

    [Fact]
    public void Local_config_redirects_to_another_location_with_expansion()
    {
        var synced = Path.Combine(_root, "cloud", "Preferis");
        Directory.CreateDirectory(synced);
        File.WriteAllText(Path.Combine(synced, "config.json"), Fixtures.ConfigSample);
        File.WriteAllText(Paths.LocalConfigPath, """{ "configPath": "~/cloud/Preferis/config.json" }""");

        var result = Loader().Load();

        Assert.Null(result.Error);
        // "~/" expands with the separator written in local.json; Windows reads either.
        Assert.Equal(Path.GetFullPath(Path.Combine(synced, "config.json")), Path.GetFullPath(result.ConfigPath));
        Assert.Equal("edge", result.Config.DefaultProfile);
        Assert.Equal("~/cloud/Preferis/config.json", result.Local.ConfigPath);
    }

    [Fact]
    public void A_corrupt_config_is_reported_and_replaced_by_the_default()
    {
        File.WriteAllText(Paths.DefaultConfigPath, "{ definitely not json");

        var result = Loader().Load();

        Assert.NotNull(result.Error);
        Assert.True(result.Exists);
        Assert.Empty(result.Config.Profiles);
    }

    [Fact]
    public void A_config_with_null_where_a_value_is_needed_is_reported_like_a_corrupt_one()
    {
        File.WriteAllText(Paths.DefaultConfigPath, """{ "version": 1, "profiles": [ { "id": "edge", "name": "Edge", "browser": "edge" } ], "rules": null }""");

        var result = Loader().Load();

        Assert.NotNull(result.Error);
        Assert.Contains("config.json", result.Error, StringComparison.Ordinal);
        Assert.True(result.Exists);
        Assert.Empty(result.Config.Profiles);
        Assert.Empty(result.Config.Rules);
    }

    [Fact]
    public void A_local_config_with_null_where_a_value_is_needed_is_reported_like_a_corrupt_one()
    {
        File.WriteAllText(Paths.LocalConfigPath, """{ "profileTargets": { "work": null } }""");
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);

        var result = Loader().Load();

        Assert.NotNull(result.Error);
        Assert.Contains("local.json", result.Error, StringComparison.Ordinal);
        Assert.Null(result.Local.ProfileTargets);
        Assert.Equal(6, result.Config.Profiles.Count);
        Assert.Null(Loader().LoadLocal().ProfileTargets);
    }

    [Fact]
    public void An_existing_config_with_null_where_a_value_is_needed_is_not_adopted()
    {
        var synced = Path.Combine(_root, "cloud", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(synced)!);
        File.WriteAllText(synced, """{ "profiles": [ null ] }""");

        Assert.Throws<InvalidDataException>(() => Loader().UseExistingConfig(synced));
        Assert.False(File.Exists(Paths.LocalConfigPath));
    }

    [Fact]
    public void A_corrupt_local_config_is_reported_but_the_default_config_location_is_still_used()
    {
        File.WriteAllText(Paths.LocalConfigPath, "nope");
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);

        var result = Loader().Load();

        Assert.NotNull(result.Error);
        Assert.Equal(6, result.Config.Profiles.Count);
    }

    [Fact]
    public void Save_writes_atomically_and_round_trips()
    {
        var loader = Loader();
        var config = PreferisConfig.CreateDefault();
        config.DefaultProfile = "edge";

        loader.Save(config, Paths.DefaultConfigPath);

        Assert.Equal("edge", loader.Load().Config.DefaultProfile);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Relocating_writes_the_config_there_points_local_json_at_it_and_removes_the_old_file()
    {
        var loader = Loader();
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);
        var config = loader.Load().Config;
        config.DefaultProfile = "chrome-work";
        var cloud = Path.Combine(_root, "cloud", "Preferis", "config.json");

        loader.RelocateConfig(config, Paths.DefaultConfigPath, cloud, overwrite: false);

        Assert.False(File.Exists(Paths.DefaultConfigPath));
        var reloaded = loader.Load();
        Assert.Equal(cloud, reloaded.ConfigPath);
        Assert.Equal("chrome-work", reloaded.Config.DefaultProfile);
    }

    [Fact]
    public void Relocating_keeps_the_other_local_settings()
    {
        File.WriteAllText(Paths.LocalConfigPath, """{ "logging": { "level": "trace" }, "futureSetting": 1 }""");
        var loader = Loader();

        loader.RelocateConfig(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath, Path.Combine(_root, "cloud", "config.json"), overwrite: false);

        var local = loader.LoadLocal();
        Assert.Equal("trace", local.Logging?.Level);
        Assert.True(local.ExtensionData?.ContainsKey("futureSetting"));
    }

    [Fact]
    public void Relocating_onto_an_existing_file_needs_consent()
    {
        var loader = Loader();
        var cloud = Path.Combine(_root, "cloud.json");
        File.WriteAllText(cloud, Fixtures.ConfigSample);

        Assert.Throws<IOException>(() => loader.RelocateConfig(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath, cloud, overwrite: false));
        Assert.Equal(6, ConfigSerializer.Deserialize(File.ReadAllText(cloud)).Profiles.Count);

        loader.RelocateConfig(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath, cloud, overwrite: true);
        Assert.Empty(ConfigSerializer.Deserialize(File.ReadAllText(cloud)).Profiles);
    }

    [Fact]
    public void Relocating_back_to_the_default_location_removes_the_redirection()
    {
        var loader = Loader();
        var cloud = Path.Combine(_root, "cloud.json");
        loader.RelocateConfig(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath, cloud, overwrite: false);

        loader.RelocateConfig(PreferisConfig.CreateDefault(), cloud, Paths.DefaultConfigPath, overwrite: false);

        Assert.Null(loader.LoadLocal().ConfigPath);
        Assert.Equal(Paths.DefaultConfigPath, loader.Load().ConfigPath);
    }

    [Fact]
    public void Relocating_onto_another_name_of_the_same_file_keeps_it()
    {
        if (OperatingSystem.IsWindows())
        {
            // Creating symbolic links needs extra rights on Windows.
            return;
        }
        var loader = Loader();
        File.WriteAllText(Paths.DefaultConfigPath, Fixtures.ConfigSample);
        var alias = Path.Combine(_root, "alias");
        Directory.CreateSymbolicLink(alias, _root);
        var config = loader.Load().Config;

        loader.RelocateConfig(config, Paths.DefaultConfigPath, Path.Combine(alias, "config.json"), overwrite: true);

        var reloaded = loader.Load();
        Assert.True(reloaded.Exists);
        Assert.Null(reloaded.Error);
        Assert.Equal(6, reloaded.Config.Profiles.Count);
    }

    [Fact]
    public void Relocating_away_from_a_shared_location_leaves_the_file_for_the_other_machines()
    {
        var loader = Loader();
        var cloud = Path.Combine(_root, "cloud.json");
        var elsewhere = Path.Combine(_root, "elsewhere.json");
        loader.RelocateConfig(PreferisConfig.CreateDefault(), Paths.DefaultConfigPath, cloud, overwrite: false);

        loader.RelocateConfig(PreferisConfig.CreateDefault(), cloud, elsewhere, overwrite: false);
        loader.RelocateConfig(PreferisConfig.CreateDefault(), elsewhere, Paths.DefaultConfigPath, overwrite: false);

        Assert.True(File.Exists(cloud));
        Assert.True(File.Exists(elsewhere));
    }

    [Fact]
    public void An_existing_config_elsewhere_can_be_adopted_without_touching_it()
    {
        var loader = Loader();
        var synced = Path.Combine(_root, "synced.json");
        File.WriteAllText(synced, Fixtures.ConfigSample);

        loader.UseExistingConfig(synced);

        var result = loader.Load();
        Assert.Equal(synced, result.ConfigPath);
        Assert.Equal(6, result.Config.Profiles.Count);
        Assert.Equal(Fixtures.ConfigSample, File.ReadAllText(synced));
    }

    [Fact]
    public void A_file_that_is_not_a_configuration_is_not_adopted()
    {
        var loader = Loader();
        var other = Path.Combine(_root, "other.json");
        File.WriteAllText(other, "[1, 2, 3]");

        Assert.Throws<InvalidDataException>(() => loader.UseExistingConfig(other));
        Assert.Throws<FileNotFoundException>(() => loader.UseExistingConfig(Path.Combine(_root, "missing.json")));
        Assert.Null(loader.LoadLocal().ConfigPath);
    }
}
