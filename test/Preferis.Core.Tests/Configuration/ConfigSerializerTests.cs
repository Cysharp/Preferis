using System.Text.Json;
using Preferis.Core.Configuration;

namespace Preferis.Core.Tests.Configuration;

public class ConfigSerializerTests
{
    [Fact]
    public void Deserializes_the_sample_config()
    {
        var config = ConfigSerializer.Deserialize(Fixtures.ConfigSample);

        Assert.Equal(1, config.Version);
        Assert.Equal("edge", config.DefaultProfile);
        Assert.Equal(6, config.Profiles.Count);
        Assert.Equal(4, config.Rules.Count);
        Assert.Equal("auto", config.Settings?.Language);
        Assert.Equal("cursor", config.Settings?.Picker?.Position);
    }

    [Fact]
    public void Browser_reference_accepts_a_plain_id_or_a_per_os_object()
    {
        var config = ConfigSerializer.Deserialize(Fixtures.ConfigSample);

        var edge = config.Profiles.Single(p => p.Id == "edge").Browser!;
        Assert.Equal("edge", edge.For(HostPlatform.Windows));
        Assert.Equal("edge", edge.For(HostPlatform.MacOS));

        var system = config.Profiles.Single(p => p.Id == "system-browser").Browser!;
        Assert.Equal("edge", system.For(HostPlatform.Windows));
        Assert.Equal("safari", system.For(HostPlatform.MacOS));
    }

    [Fact]
    public void Browser_profile_reference_keeps_all_identifiers()
    {
        var config = ConfigSerializer.Deserialize(Fixtures.ConfigSample);
        var work = config.Profiles.Single(p => p.Id == "chrome-work");

        Assert.Equal("Profile 1", work.BrowserProfile?.Directory);
        Assert.Equal("Work", work.BrowserProfile?.Name);
        Assert.Equal("me@work.example.com", work.BrowserProfile?.Email);
        Assert.Equal("edge", work.Fallback);
        Assert.True(config.Profiles.Single(p => p.Id == "edge-private").Private);
    }

    [Fact]
    public void Command_target_is_per_os()
    {
        var config = ConfigSerializer.Deserialize(Fixtures.ConfigSample);
        var tool = config.Profiles.Single(p => p.Id == "custom-tool");

        Assert.Null(tool.Browser);
        Assert.Equal("%LOCALAPPDATA%\\Programs\\Foo\\foo.exe", tool.Command?.For(HostPlatform.Windows)?.Path);
        Assert.Equal(["--open", "{url}"], tool.Command?.For(HostPlatform.Windows)?.Args);
        Assert.Equal("/Applications/Foo.app/Contents/MacOS/Foo", tool.Command?.For(HostPlatform.MacOS)?.Path);
    }

    [Fact]
    public void Conditions_deserialize_into_a_typed_tree()
    {
        var config = ConfigSerializer.Deserialize(Fixtures.ConfigSample);

        var safelinks = Assert.IsType<HostCondition>(config.Rules[0].Condition);
        Assert.Equal(MatchKind.Wildcard, safelinks.Match);
        Assert.Equal("*.safelinks.protection.outlook.com", safelinks.Pattern);

        var admin = Assert.IsType<UrlCondition>(config.Rules[1].Condition);
        Assert.Equal(MatchKind.StartsWith, admin.Match);
        Assert.True(admin.IgnoreCase);

        var split = Assert.IsType<OrCondition>(config.Rules[2].Condition);
        Assert.Equal(2, split.Conditions.Count);
        Assert.All(split.Conditions, c => Assert.Equal(MatchKind.Exact, Assert.IsType<HostCondition>(c).Match));

        var login = Assert.IsType<HostCondition>(config.Rules[3].Condition);
        Assert.Equal(MatchKind.Regex, login.Match);
    }

    [Fact]
    public void Actions_deserialize_into_a_typed_list()
    {
        var config = ConfigSerializer.Deserialize(Fixtures.ConfigSample);

        var rewrite = Assert.IsType<RewriteAction>(config.Rules[0].Actions[0]);
        Assert.Equal(RewriteMode.QueryParameter, rewrite.Mode);
        Assert.Equal("url", rewrite.Parameter);
        Assert.False(rewrite.IsTerminal);
        Assert.IsType<RerouteAction>(config.Rules[0].Actions[1]);

        var open = Assert.IsType<OpenAction>(config.Rules[1].Actions[0]);
        Assert.Equal(["chrome-work"], open.Profiles);
        Assert.True(open.IsTerminal);

        var catchAll = Assert.IsType<OpenAction>(config.CatchAll.Actions.Single());
        Assert.True(catchAll.Profiles is null or { Count: 0 });
    }

    [Fact]
    public void Rule_is_enabled_when_the_property_is_omitted()
    {
        const string json = """
            { "version": 1, "profiles": [], "catchAll": { "actions": [ { "type": "none" } ] },
              "rules": [ { "id": "r", "name": "r", "condition": { "type": "host", "match": "equals", "pattern": "a" }, "actions": [ { "type": "none" } ] } ] }
            """;

        var config = ConfigSerializer.Deserialize(json);

        Assert.True(config.Rules[0].Enabled);
    }

    [Fact]
    public void Type_discriminator_may_come_after_other_properties()
    {
        const string json = """
            { "version": 1, "profiles": [], "catchAll": { "actions": [ { "profiles": ["edge"], "type": "open" } ] },
              "rules": [ { "id": "r", "name": "r", "condition": { "match": "wildcard", "pattern": "*.example.com", "type": "host" }, "actions": [ { "type": "none" } ] } ] }
            """;

        var config = ConfigSerializer.Deserialize(json);

        var host = Assert.IsType<HostCondition>(config.Rules[0].Condition);
        Assert.Equal("*.example.com", host.Pattern);
        Assert.Equal(["edge"], Assert.IsType<OpenAction>(config.CatchAll.Actions[0]).Profiles);
    }

    [Fact]
    public void Unknown_condition_type_survives_a_round_trip()
    {
        const string json = """
            { "version": 1, "profiles": [], "catchAll": { "actions": [ { "type": "none" } ] },
              "rules": [ { "id": "r", "name": "r", "condition": { "type": "geo", "country": "JP" }, "actions": [ { "type": "none" } ] } ] }
            """;

        var config = ConfigSerializer.Deserialize(json);
        var unknown = Assert.IsType<UnknownCondition>(config.Rules[0].Condition);
        Assert.Equal("geo", unknown.Type);

        var reparsed = JsonDocument.Parse(ConfigSerializer.Serialize(config));
        var condition = reparsed.RootElement.GetProperty("rules")[0].GetProperty("condition");
        Assert.Equal("geo", condition.GetProperty("type").GetString());
        Assert.Equal("JP", condition.GetProperty("country").GetString());
    }

    [Fact]
    public void Unknown_action_type_survives_a_round_trip()
    {
        const string json = """
            { "version": 1, "profiles": [], "catchAll": { "actions": [ { "type": "notify", "channel": "slack" }, { "type": "none" } ] }, "rules": [] }
            """;

        var config = ConfigSerializer.Deserialize(json);
        var unknown = Assert.IsType<UnknownAction>(config.CatchAll.Actions[0]);
        Assert.Equal("notify", unknown.Type);

        var reparsed = JsonDocument.Parse(ConfigSerializer.Serialize(config));
        var action = reparsed.RootElement.GetProperty("catchAll").GetProperty("actions")[0];
        Assert.Equal("slack", action.GetProperty("channel").GetString());
    }

    [Fact]
    public void Unknown_properties_survive_a_round_trip()
    {
        const string json = """
            { "version": 1, "x-future": { "a": 1 }, "profiles": [ { "id": "p", "name": "p", "browser": "edge", "color": "red" } ],
              "rules": [ { "id": "r", "name": "r", "priority": 5, "condition": { "type": "host", "match": "equals", "pattern": "a" }, "actions": [ { "type": "none" } ] } ],
              "catchAll": { "actions": [ { "type": "none" } ] } }
            """;

        var reparsed = JsonDocument.Parse(ConfigSerializer.Serialize(ConfigSerializer.Deserialize(json))).RootElement;

        Assert.Equal(1, reparsed.GetProperty("x-future").GetProperty("a").GetInt32());
        Assert.Equal("red", reparsed.GetProperty("profiles")[0].GetProperty("color").GetString());
        Assert.Equal(5, reparsed.GetProperty("rules")[0].GetProperty("priority").GetInt32());
    }

    [Fact]
    public void Serialization_is_stable_after_one_normalization()
    {
        var once = ConfigSerializer.Serialize(ConfigSerializer.Deserialize(Fixtures.ConfigSample));
        var twice = ConfigSerializer.Serialize(ConfigSerializer.Deserialize(once));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Enum_values_are_written_in_camel_case()
    {
        var config = PreferisConfig.CreateDefault();
        config.Rules.Add(new Rule
        {
            Id = "r",
            Name = "r",
            Condition = new UrlCondition { Match = MatchKind.StartsWith, Pattern = "https://a/" },
            Actions = [new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = "url" }, new OpenAction()],
        });

        var json = ConfigSerializer.Serialize(config);

        Assert.Contains("\"match\": \"startsWith\"", json);
        Assert.Contains("\"mode\": \"queryParameter\"", json);
        Assert.Contains("\"type\": \"url\"", json);
        Assert.Contains("\"type\": \"rewrite\"", json);
    }

    [Fact]
    public void Default_config_has_an_open_catch_all_and_nothing_else()
    {
        var config = PreferisConfig.CreateDefault();

        Assert.Equal(PreferisConfig.CurrentVersion, config.Version);
        Assert.Null(config.DefaultProfile);
        Assert.Empty(config.Profiles);
        Assert.Empty(config.Rules);
        Assert.IsType<OpenAction>(config.CatchAll.Actions.Single());
    }

    [Fact]
    public void Deserializes_the_local_config()
    {
        var local = ConfigSerializer.DeserializeLocal(Fixtures.LocalSample);

        Assert.Equal("~/Library/CloudStorage/OneDrive-Example/Apps/Preferis/config.json", local.ConfigPath);
        Assert.Equal("/Applications/Google Chrome Beta.app", local.BrowserOverrides?["chrome"].Path);
        Assert.Equal("error", local.Logging?.Level);
        Assert.False(local.Logging?.History);
        Assert.Equal(50, local.Logging?.HistorySize);
    }

    [Fact]
    public void Invalid_json_throws_a_json_exception()
    {
        Assert.ThrowsAny<JsonException>(() => ConfigSerializer.Deserialize("{ not json"));
    }
}
