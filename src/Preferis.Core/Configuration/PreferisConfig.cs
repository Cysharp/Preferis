using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration;

/// <summary>Root of <c>config.json</c>: the machine independent, shareable configuration. See docs/Configuration.md §2.</summary>
public sealed class PreferisConfig
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Id of the profile used by <c>open</c> actions that do not name a profile.</summary>
    public string? DefaultProfile { get; set; }

    public List<Profile> Profiles { get; set; } = [];

    /// <summary>Rules in evaluation order.</summary>
    public List<Rule> Rules { get; set; } = [];

    public CatchAllRule CatchAll { get; set; } = new();

    public AppSettings? Settings { get; set; }

    // Properties this version does not know about are kept and written back, so that a newer
    // Preferis sharing the same synced file does not lose its settings.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>The configuration used when no file exists yet: no profiles, no rules, an open catch-all.</summary>
    public static PreferisConfig CreateDefault() => new();
}

/// <summary>The rule applied when no other rule matched. Always present; only its actions are editable.</summary>
public sealed class CatchAllRule
{
    public List<RoutingAction> Actions { get; set; } = [new OpenAction()];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class AppSettings
{
    public PickerSettings? Picker { get; set; }

    /// <summary><c>auto</c>, <c>en</c> or <c>ja</c>.</summary>
    public string? Language { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class PickerSettings
{
    /// <summary><c>cursor</c> or <c>center</c>.</summary>
    public string Position { get; set; } = "cursor";

    /// <summary>Seconds of inactivity after which the first candidate is chosen. 0 disables.</summary>
    public double AutoSelectSeconds { get; set; }

    public bool RememberChoiceDefault { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
