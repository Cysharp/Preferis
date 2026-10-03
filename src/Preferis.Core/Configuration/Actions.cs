using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration;

/// <summary>One step of a rule's action list. The JSON <c>type</c> property selects the concrete class.</summary>
public abstract class RoutingAction
{
    [JsonPropertyOrder(-10)]
    public abstract string Type { get; }

    /// <summary>Terminal actions end routing; non-terminal ones hand the (possibly rewritten) URL to the next action.</summary>
    /// <remarks>The source generator does not inherit <see cref="JsonIgnoreAttribute"/> from this declaration, so every override repeats it.</remarks>
    [JsonIgnore]
    public abstract bool IsTerminal { get; }

    /// <summary>
    /// Properties a newer Preferis added to a known type, written back as they are (docs/Configuration.md §4).
    /// Unused by <see cref="UnknownAction"/>, whose <see cref="UnknownAction.Raw"/> already holds the whole object.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Open with the listed profiles: one launches directly, several show the picker, none means the default profile.</summary>
public sealed class OpenAction : RoutingAction
{
    public override string Type => "open";

    [JsonIgnore]
    public override bool IsTerminal => true;

    public List<string>? Profiles { get; set; }
}

/// <summary>Hand the URL to the OS default handler (guarded against pointing back at Preferis).</summary>
public sealed class SystemAction : RoutingAction
{
    public override string Type => "system";

    [JsonIgnore]
    public override bool IsTerminal => true;
}

/// <summary>Do nothing.</summary>
public sealed class NoneAction : RoutingAction
{
    public override string Type => "none";

    [JsonIgnore]
    public override bool IsTerminal => true;
}

public enum RewriteMode
{
    /// <summary>Regex replace over the absolute URL.</summary>
    [JsonStringEnumMemberName("regex")]
    Regex,

    /// <summary>Take a query parameter's (URL decoded) value as the new URL.</summary>
    [JsonStringEnumMemberName("queryParameter")]
    QueryParameter,
}

/// <summary>Rewrite the URL and continue with the next action.</summary>
public sealed class RewriteAction : RoutingAction
{
    public override string Type => "rewrite";

    [JsonIgnore]
    public override bool IsTerminal => false;

    public RewriteMode Mode { get; set; }

    /// <summary>Regex pattern (<see cref="RewriteMode.Regex"/>).</summary>
    public string? Pattern { get; set; }

    /// <summary>Regex replacement (<see cref="RewriteMode.Regex"/>).</summary>
    public string? Replacement { get; set; }

    /// <summary>Query parameter name (<see cref="RewriteMode.QueryParameter"/>).</summary>
    public string? Parameter { get; set; }
}

/// <summary>Start over from the first rule with the current URL.</summary>
public sealed class RerouteAction : RoutingAction
{
    public override string Type => "reroute";

    [JsonIgnore]
    public override bool IsTerminal => true;
}

/// <summary>An action whose <c>type</c> this version does not understand. Preserved verbatim; see <see cref="UnknownCondition"/>.</summary>
public sealed class UnknownAction : RoutingAction
{
    public UnknownAction(string type, JsonElement raw)
    {
        Type = type;
        Raw = raw;
    }

    public override string Type { get; }

    // Treated as terminal so that a rule with an unknown action does not fall through to other rules
    // and silently route somewhere the newer configuration did not intend.
    [JsonIgnore]
    public override bool IsTerminal => true;

    [JsonIgnore]
    public JsonElement Raw { get; }
}
