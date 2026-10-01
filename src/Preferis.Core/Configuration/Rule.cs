using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration;

/// <summary>A routing rule: a condition tree and the actions to run when it matches. See docs/Design.md §6.</summary>
public sealed class Rule
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public Condition? Condition { get; set; }

    public List<RoutingAction> Actions { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
