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

    [Theory]
    [InlineData("""{ "type": "and", "conditions": [ { "type": "host", "match": "equals", "pattern": "a" } ], "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "or", "conditions": [ { "type": "host", "match": "equals", "pattern": "a" } ], "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "not", "condition": { "type": "host", "match": "equals", "pattern": "a" }, "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "futureSetting": { "level": 2 }, "type": "host", "match": "equals", "pattern": "a" }""")]
    [InlineData("""{ "type": "url", "match": "startsWith", "pattern": "https://a/", "ignoreCase": false, "futureSetting": { "level": 2 } }""")]
    public void Unknown_properties_of_known_conditions_survive_a_round_trip(string condition)
    {
        var json = $$"""
            { "version": 1, "profiles": [], "catchAll": { "actions": [ { "type": "none" } ] },
              "rules": [ { "id": "r", "name": "r", "condition": {{condition}}, "actions": [ { "type": "none" } ] } ] }
            """;
        using var original = JsonDocument.Parse(condition);

        var config = ConfigSerializer.Deserialize(json);
        using var reparsed = JsonDocument.Parse(ConfigSerializer.Serialize(config));
        var written = reparsed.RootElement.GetProperty("rules")[0].GetProperty("condition");

        Assert.Equal(["futureSetting"], config.Rules[0].Condition!.ExtensionData?.Keys);
        Assert.Equal(2, written.GetProperty("futureSetting").GetProperty("level").GetInt32());
        Assert.Equal(original.RootElement.GetProperty("type").GetString(), Assert.Single(written.EnumerateObject(), p => p.Name == "type").Value.GetString());
    }

    [Theory]
    [InlineData("""{ "type": "open", "profiles": ["edge"], "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "system", "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "none", "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "type": "rewrite", "mode": "queryParameter", "parameter": "url", "futureSetting": { "level": 2 } }""")]
    [InlineData("""{ "futureSetting": { "level": 2 }, "type": "reroute" }""")]
    public void Unknown_properties_of_known_actions_survive_a_round_trip(string action)
    {
        var json = $$"""
            { "version": 1, "profiles": [], "catchAll": { "actions": [ {{action}} ] },
              "rules": [ { "id": "r", "name": "r", "condition": { "type": "host", "match": "equals", "pattern": "a" }, "actions": [ {{action}} ] } ] }
            """;
        using var original = JsonDocument.Parse(action);

        var config = ConfigSerializer.Deserialize(json);
        using var reparsed = JsonDocument.Parse(ConfigSerializer.Serialize(config));

        Assert.Equal(["futureSetting"], config.CatchAll.Actions[0].ExtensionData?.Keys);
        foreach (var written in new[] { reparsed.RootElement.GetProperty("catchAll").GetProperty("actions")[0], reparsed.RootElement.GetProperty("rules")[0].GetProperty("actions")[0] })
        {
            Assert.Equal(2, written.GetProperty("futureSetting").GetProperty("level").GetInt32());
            Assert.Equal(original.RootElement.GetProperty("type").GetString(), Assert.Single(written.EnumerateObject(), p => p.Name == "type").Value.GetString());
        }
    }

    [Fact]
    public void Serialization_is_stable_after_one_normalization()
    {
        var once = ConfigSerializer.Serialize(ConfigSerializer.Deserialize(Fixtures.ConfigSample));
        var twice = ConfigSerializer.Serialize(ConfigSerializer.Deserialize(once));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Only_documented_properties_are_written()
    {
        // From docs/Configuration.md §2. Anything else (such as the engine's IsTerminal) would look like a setting.
        string[] documented =
        [
            "version", "defaultProfile", "profiles", "rules", "catchAll", "settings",
            "id", "name", "browser", "browserProfile", "private", "extraArgs", "command", "fallback", "windows", "macos", "path", "args",
            "directory", "email", "enabled", "condition", "conditions", "actions", "type", "match", "pattern", "ignoreCase",
            "mode", "replacement", "parameter", "picker", "position", "autoSelectSeconds", "rememberChoiceDefault", "language",
        ];
        using var written = JsonDocument.Parse(ConfigSerializer.Serialize(ConfigSerializer.Deserialize(Fixtures.ConfigSample)));

        Assert.Empty(PropertyNames(written.RootElement).Except(documented));
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };

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

        var targets = local.ProfileTargets!;
        Assert.Equal("safari", targets["personal"].Browser);
        Assert.Equal("Profile 2", targets["work"].BrowserProfile?.Directory);
        Assert.True(targets["work"].Private);
        Assert.Equal(["--new-window"], targets["work"].ExtraArgs);
        Assert.Equal("/usr/local/bin/tool", targets["tool"].Command?.Path);
        Assert.Equal(["--open", "{url}"], targets["tool"].Command?.Args);
    }

    [Fact]
    public void Profile_targets_and_previous_ids_round_trip_with_unknown_properties()
    {
        var local = ConfigSerializer.DeserializeLocal(Fixtures.LocalSample);
        var config = ConfigSerializer.Deserialize("""{ "profiles": [ { "id": "work", "name": "Work", "previousIds": ["microsoft-edge", "edge"], "browser": "edge" } ] }""");

        var localAgain = ConfigSerializer.DeserializeLocal(ConfigSerializer.SerializeLocal(local));
        var configAgain = ConfigSerializer.Deserialize(ConfigSerializer.Serialize(config));

        Assert.True(localAgain.ProfileTargets?["tool"].ExtensionData?.ContainsKey("label"));
        Assert.Equal(["microsoft-edge", "edge"], configAgain.Profiles.Single().PreviousIds);
        Assert.DoesNotContain("previousIds", ConfigSerializer.Serialize(ConfigSerializer.Deserialize("""{ "profiles": [ { "id": "work", "name": "Work" } ] }""")), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_json_throws_a_json_exception()
    {
        Assert.ThrowsAny<JsonException>(() => ConfigSerializer.Deserialize("{ not json"));
    }

    // Valid JSON, but not a configuration that can be routed with: null where the model has no value to fall back on.
    [Theory]
    [InlineData("""{ "profiles": null }""")]
    [InlineData("""{ "rules": null }""")]
    [InlineData("""{ "catchAll": null }""")]
    [InlineData("""{ "catchAll": { "actions": null } }""")]
    [InlineData("""{ "version": null }""")]
    [InlineData("""{ "profiles": [ { "id": null, "name": "Edge", "browser": "edge" } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "edge", "name": null, "browser": "edge" } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "tool", "name": "Tool", "command": { "windows": { "path": null, "args": [] } } } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "tool", "name": "Tool", "command": { "macos": { "path": "/bin/tool", "args": null } } } ] }""")]
    [InlineData("""{ "rules": [ { "id": null, "name": "r", "actions": [ { "type": "none" } ] } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": null, "actions": [ { "type": "none" } ] } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "enabled": null, "actions": [ { "type": "none" } ] } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "actions": null } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "and", "conditions": null }, "actions": [ { "type": "none" } ] } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "not", "condition": { "type": "or", "conditions": null } }, "actions": [ { "type": "none" } ] } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "host", "match": "equals", "pattern": null }, "actions": [ { "type": "none" } ] } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "url", "match": null, "pattern": "https://a/" }, "actions": [ { "type": "none" } ] } ] }""")]
    [InlineData("""{ "settings": { "picker": { "position": null } } }""")]
    public void Null_where_a_value_is_needed_is_rejected(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ConfigSerializer.Deserialize(json));
    }

    // The serializer's own check covers properties only; the items of a list need ours.
    [Theory]
    [InlineData("""{ "profiles": [ null ] }""", "$.profiles[0]")]
    [InlineData("""{ "profiles": [ { "id": "a", "name": "A", "previousIds": [ "b", null ] } ] }""", "$.profiles[0].previousIds[1]")]
    [InlineData("""{ "profiles": [ { "id": "a", "name": "A", "browser": "edge", "extraArgs": [ null ] } ] }""", "$.profiles[0].extraArgs[0]")]
    [InlineData("""{ "profiles": [ { "id": "a", "name": "A", "command": { "macos": { "path": "/bin/a", "args": [ "{url}", null ] } } } ] }""", "$.profiles[0].command.macos.args[1]")]
    [InlineData("""{ "rules": [ null ] }""", "$.rules[0]")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "actions": [ { "type": "none" }, null ] } ] }""", "$.rules[0].actions[1]")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "actions": [ { "type": "open", "profiles": [ null ] } ] } ] }""", "$.rules[0].actions[0].profiles[0]")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "or", "conditions": [ { "type": "host", "match": "equals", "pattern": "a" }, null ] }, "actions": [ { "type": "none" } ] } ] }""", "$.rules[0].condition.conditions[1]")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "not", "condition": { "type": "and", "conditions": [ { "type": "or", "conditions": [ null ] } ] } }, "actions": [ { "type": "none" } ] } ] }""", "$.rules[0].condition.condition.conditions[0].conditions[0]")]
    [InlineData("""{ "catchAll": { "actions": [ null ] } }""", "$.catchAll.actions[0]")]
    public void Null_items_are_rejected_with_where_they_are(string json, string path)
    {
        var error = Assert.ThrowsAny<JsonException>(() => ConfigSerializer.Deserialize(json));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_a_stream_rejects_null_items_too()
    {
        using var json = new MemoryStream("""{ "rules": [ null ] }"""u8.ToArray());

        Assert.ThrowsAny<JsonException>(() => ConfigSerializer.Deserialize(json));
    }

    // Optional properties (docs/Configuration.md §2): null means the same as leaving them out.
    [Theory]
    [InlineData("""{ "defaultProfile": null, "settings": null }""")]
    [InlineData("""{ "settings": { "picker": null, "language": null } }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "name": "A", "previousIds": null, "browser": null, "browserProfile": null, "extraArgs": null, "command": null, "fallback": null } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "name": "A", "browser": { "windows": "edge", "macos": null }, "browserProfile": { "directory": null, "name": "Work", "email": null } } ] }""")]
    [InlineData("""{ "profiles": [ { "id": "a", "name": "A", "command": { "windows": null, "macos": { "path": "/bin/a", "args": [] } } } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": null, "actions": [ { "type": "open", "profiles": null } ] } ] }""")]
    [InlineData("""{ "rules": [ { "id": "r", "name": "r", "condition": { "type": "not", "condition": null }, "actions": [ { "type": "rewrite", "mode": "regex", "pattern": null, "replacement": null, "parameter": null } ] } ] }""")]
    public void Null_is_accepted_where_a_value_is_optional(string json)
    {
        var config = ConfigSerializer.Deserialize(json);

        Assert.NotNull(config.CatchAll.Actions);
    }

    [Fact]
    public void Nulls_inside_what_this_version_does_not_know_are_kept()
    {
        const string json = """
            { "x-future": null, "profiles": [ { "id": "a", "name": "A", "browser": "edge", "color": null } ],
              "rules": [ { "id": "r", "name": "r", "condition": { "type": "geo", "countries": [ null ] }, "actions": [ { "type": "notify", "channel": null }, { "type": "none" } ] } ],
              "catchAll": { "actions": [ { "type": "none" } ] } }
            """;

        var reparsed = JsonDocument.Parse(ConfigSerializer.Serialize(ConfigSerializer.Deserialize(json))).RootElement;

        Assert.Equal(JsonValueKind.Null, reparsed.GetProperty("x-future").ValueKind);
        Assert.Equal(JsonValueKind.Null, reparsed.GetProperty("profiles")[0].GetProperty("color").ValueKind);
        Assert.Equal(JsonValueKind.Null, reparsed.GetProperty("rules")[0].GetProperty("condition").GetProperty("countries")[0].ValueKind);
        Assert.Equal(JsonValueKind.Null, reparsed.GetProperty("rules")[0].GetProperty("actions")[0].GetProperty("channel").ValueKind);
    }

    [Theory]
    [InlineData("""{ "browserOverrides": { "chrome": null } }""", "$.browserOverrides['chrome']")]
    [InlineData("""{ "profileTargets": { "work": null } }""", "$.profileTargets['work']")]
    [InlineData("""{ "profileTargets": { "work": { "browser": "edge", "extraArgs": [ null ] } } }""", "$.profileTargets['work'].extraArgs[0]")]
    [InlineData("""{ "profileTargets": { "tool": { "command": { "path": "/bin/tool", "args": [ null ] } } } }""", "$.profileTargets['tool'].command.args[0]")]
    public void Null_items_of_the_local_config_are_rejected_with_where_they_are(string json, string path)
    {
        var error = Assert.ThrowsAny<JsonException>(() => ConfigSerializer.DeserializeLocal(json));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "logging": { "level": null } }""")]
    [InlineData("""{ "profileTargets": { "tool": { "command": { "path": null, "args": [] } } } }""")]
    [InlineData("""{ "profileTargets": { "tool": { "command": { "path": "/bin/tool", "args": null } } } }""")]
    public void Null_where_the_local_config_needs_a_value_is_rejected(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ConfigSerializer.DeserializeLocal(json));
    }

    [Theory]
    [InlineData("""{ "configPath": null, "browserOverrides": null, "profileTargets": null, "logging": null }""")]
    [InlineData("""{ "browserOverrides": { "chrome": { "path": null, "userDataDir": null } } }""")]
    [InlineData("""{ "profileTargets": { "work": { "browser": null, "browserProfile": null, "extraArgs": null, "command": null } } }""")]
    public void Null_is_accepted_where_the_local_config_has_it_optional(string json)
    {
        Assert.NotNull(ConfigSerializer.DeserializeLocal(json));
    }
}
