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

        Assert.Equal("/Users/me/Library/Application Support/Preferis/local.json", paths.LocalConfigPath);
        Assert.Equal("/Users/me/Library/Application Support/Preferis/config.json", paths.DefaultConfigPath);
        Assert.Equal("/Users/me/Library/Logs/Preferis", paths.LogDirectory);
    }
}

public class ConfigLoaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("preferis-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ConfigPaths Paths => new(Path.Combine(_root, "local.json"), Path.Combine(_root, "config.json"), Path.Combine(_root, "logs"));

    private ConfigLoader Loader(Platform platform = Platform.MacOS) => new(Paths, platform, _ => null, _root);

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
        Assert.Equal(Path.Combine(synced, "config.json"), result.ConfigPath);
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
}
