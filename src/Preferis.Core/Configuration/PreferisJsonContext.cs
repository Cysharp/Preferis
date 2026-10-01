using System.Text.Json.Serialization;
using Preferis.Core.Configuration.Json;

namespace Preferis.Core.Configuration;

/// <summary>Source generated JSON metadata for everything Preferis reads and writes. Keeps serialization reflection free for NativeAOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true,
    Converters = [typeof(ConditionJsonConverter), typeof(RoutingActionJsonConverter), typeof(BrowserReferenceJsonConverter)])]
[JsonSerializable(typeof(PreferisConfig))]
[JsonSerializable(typeof(LocalConfig))]
[JsonSerializable(typeof(AndCondition))]
[JsonSerializable(typeof(OrCondition))]
[JsonSerializable(typeof(NotCondition))]
[JsonSerializable(typeof(HostCondition))]
[JsonSerializable(typeof(UrlCondition))]
[JsonSerializable(typeof(OpenAction))]
[JsonSerializable(typeof(SystemAction))]
[JsonSerializable(typeof(NoneAction))]
[JsonSerializable(typeof(RewriteAction))]
[JsonSerializable(typeof(RerouteAction))]
public sealed partial class PreferisJsonContext : JsonSerializerContext;
