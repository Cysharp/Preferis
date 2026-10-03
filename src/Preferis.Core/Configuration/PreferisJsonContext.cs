using System.Text.Json.Serialization;
using Preferis.Core.Configuration.Json;
using Preferis.Core.Picker;

namespace Preferis.Core.Configuration;

/// <summary>Source generated JSON metadata for everything Preferis reads and writes. Keeps serialization reflection free for NativeAOT.</summary>
/// <remarks>
/// <see cref="JsonSourceGenerationOptionsAttribute.RespectNullableAnnotations"/>: an explicit <c>null</c> for a property the
/// model declares non-nullable (<c>"rules": null</c>, a rule whose <c>id</c> is null) fails the read like malformed JSON,
/// instead of reaching routing or the settings as a null they dereference. Optional properties are the nullable ones.
/// It does not look into lists and dictionaries; <see cref="ConfigStructure"/> does.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
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
[JsonSerializable(typeof(PickerRequest))]
[JsonSerializable(typeof(PickerResponse))]
public sealed partial class PreferisJsonContext : JsonSerializerContext;
