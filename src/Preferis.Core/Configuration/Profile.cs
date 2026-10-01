using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration;

/// <summary>A launch target: a browser (optionally with a browser profile) or an arbitrary command. See docs/Configuration.md §2.3.</summary>
public sealed class Profile
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Catalog browser id, either one id for every OS or one per OS. Exclusive with <see cref="Command"/>.</summary>
    public BrowserReference? Browser { get; set; }

    public BrowserProfileRef? BrowserProfile { get; set; }

    /// <summary>Open in the browser's private / incognito mode.</summary>
    public bool Private { get; set; }

    public List<string>? ExtraArgs { get; set; }

    /// <summary>Arbitrary executable per OS. Exclusive with <see cref="Browser"/>.</summary>
    public CommandTarget? Command { get; set; }

    /// <summary>Profile id to use when this one cannot be resolved on the current machine.</summary>
    public string? Fallback { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Reference to a catalog browser id. Serialized as a plain string when the same id applies to every OS,
/// or as <c>{ "windows": ..., "macos": ... }</c>.
/// </summary>
public sealed class BrowserReference
{
    /// <summary>Id used for every platform that has no specific entry.</summary>
    public string? Default { get; set; }

    public string? Windows { get; set; }

    public string? MacOS { get; set; }

    public BrowserReference()
    {
    }

    public BrowserReference(string id)
    {
        Default = id;
    }

    /// <summary>True when the reference is just one id, serialized as a string.</summary>
    public bool IsPlain => Windows is null && MacOS is null;

    /// <summary>The browser id to use on <paramref name="platform"/>, or null when none applies.</summary>
    public string? For(HostPlatform platform) => platform switch
    {
        HostPlatform.Windows => Windows ?? Default,
        HostPlatform.MacOS => MacOS ?? Default,
        _ => Default,
    };
}

/// <summary>
/// Identifies a browser profile by several keys so that it can be matched across machines where the
/// directory name differs. Resolution order: <see cref="Email"/>, <see cref="Name"/>, <see cref="Directory"/>.
/// </summary>
public sealed class BrowserProfileRef
{
    /// <summary>Chromium profile directory (<c>Default</c>, <c>Profile 1</c>, ...). Also the last resort when profile data cannot be read.</summary>
    public string? Directory { get; set; }

    /// <summary>Display name (Chromium <c>name</c>, Firefox <c>profiles.ini</c> <c>Name</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Signed-in account (Chromium <c>user_name</c>). The most stable key across machines.</summary>
    public string? Email { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class CommandTarget
{
    public CommandSpec? Windows { get; set; }

    // The camelCase policy would produce "macOS"; the documented key is "macos".
    [JsonPropertyName("macos")]
    public CommandSpec? MacOS { get; set; }

    public CommandSpec? For(HostPlatform platform) => platform switch
    {
        HostPlatform.Windows => Windows,
        HostPlatform.MacOS => MacOS,
        _ => null,
    };

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class CommandSpec
{
    /// <summary>Executable path. Environment variables and <c>~</c> are expanded at resolution time.</summary>
    public string Path { get; set; } = "";

    /// <summary>Arguments. <c>{url}</c> is replaced with the URL.</summary>
    public List<string> Args { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
