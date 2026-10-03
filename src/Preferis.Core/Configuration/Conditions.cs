using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration;

/// <summary>Node of a condition tree. The JSON <c>type</c> property selects the concrete class.</summary>
public abstract class Condition
{
    [JsonPropertyOrder(-10)]
    public abstract string Type { get; }

    /// <summary>
    /// Properties a newer Preferis added to a known type, written back as they are (docs/Configuration.md §4).
    /// Unused by <see cref="UnknownCondition"/>, whose <see cref="UnknownCondition.Raw"/> already holds the whole object.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class AndCondition : Condition
{
    public override string Type => "and";

    public List<Condition> Conditions { get; set; } = [];
}

public sealed class OrCondition : Condition
{
    public override string Type => "or";

    public List<Condition> Conditions { get; set; } = [];
}

public sealed class NotCondition : Condition
{
    public override string Type => "not";

    public Condition? Condition { get; set; }
}

/// <summary>How a leaf condition compares its target text with <see cref="PatternCondition.Pattern"/>.</summary>
public enum MatchKind
{
    [JsonStringEnumMemberName("regex")]
    Regex,

    [JsonStringEnumMemberName("wildcard")]
    Wildcard,

    [JsonStringEnumMemberName("startsWith")]
    StartsWith,

    [JsonStringEnumMemberName("endsWith")]
    EndsWith,

    [JsonStringEnumMemberName("contains")]
    Contains,

    [JsonStringEnumMemberName("equals")]
    Exact,
}

/// <summary>Leaf condition: a pattern applied to one part of the URL.</summary>
public abstract class PatternCondition : Condition
{
    public MatchKind Match { get; set; }

    public string Pattern { get; set; } = "";
}

/// <summary>Matches against the host. Always case insensitive.</summary>
public sealed class HostCondition : PatternCondition
{
    public override string Type => "host";
}

/// <summary>Matches against the normalized absolute URL.</summary>
public sealed class UrlCondition : PatternCondition
{
    public override string Type => "url";

    public bool IgnoreCase { get; set; } = true;
}

/// <summary>
/// A condition whose <c>type</c> this version does not understand. Never matches, and is written back
/// verbatim so that a newer Preferis sharing the file keeps its rule.
/// </summary>
public sealed class UnknownCondition : Condition
{
    public UnknownCondition(string type, JsonElement raw)
    {
        Type = type;
        Raw = raw;
    }

    public override string Type { get; }

    [JsonIgnore]
    public JsonElement Raw { get; }
}
