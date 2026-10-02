using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration;

/// <summary>Root of <c>local.json</c>: machine specific settings that are never synced. See docs/Configuration.md §3.</summary>
public sealed class LocalConfig
{
    /// <summary>Location of <c>config.json</c>. Environment variables and <c>~</c> are expanded. Null means the default location.</summary>
    public string? ConfigPath { get; set; }

    /// <summary>Browser id to override, taking precedence over detection.</summary>
    public Dictionary<string, BrowserOverride>? BrowserOverrides { get; set; }

    /// <summary>Profile id to the target this machine opens instead of the shared one (docs/Configuration.md §3.1).</summary>
    public Dictionary<string, ProfileTarget>? ProfileTargets { get; set; }

    public LoggingSettings? Logging { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class BrowserOverride
{
    /// <summary>Executable or <c>.app</c> path.</summary>
    public string? Path { get; set; }

    public string? UserDataDir { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// What a profile opens on this machine, replacing the whole shared target of <c>config.json</c>: one machine has one
/// OS, so there is nothing per OS here.
/// </summary>
public sealed class ProfileTarget
{
    /// <summary>Catalog browser id. Exclusive with <see cref="Command"/>.</summary>
    public string? Browser { get; set; }

    public BrowserProfileRef? BrowserProfile { get; set; }

    public bool Private { get; set; }

    public List<string>? ExtraArgs { get; set; }

    /// <summary>Program to run on this machine. Exclusive with <see cref="Browser"/>.</summary>
    public CommandSpec? Command { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class LoggingSettings
{
    /// <summary><c>off</c>, <c>error</c>, <c>info</c> or <c>trace</c>.</summary>
    public string Level { get; set; } = "error";

    /// <summary>Keep a local routing history (contains URLs). Opt-in.</summary>
    public bool History { get; set; }

    public int HistorySize { get; set; } = 50;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
