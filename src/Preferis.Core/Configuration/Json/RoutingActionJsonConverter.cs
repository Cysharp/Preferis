using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration.Json;

/// <summary>Dispatches on <c>type</c> to the concrete <see cref="RoutingAction"/>. See <see cref="ConditionJsonConverter"/> for the rationale.</summary>
internal sealed class RoutingActionJsonConverter : JsonConverter<RoutingAction>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(RoutingAction);

    public override RoutingAction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("An action must be a JSON object.");
        }

        var type = JsonDispatch.ReadType(element);
        return type switch
        {
            "open" => element.Deserialize(JsonDispatch.TypeInfo<OpenAction>(options)),
            "system" => element.Deserialize(JsonDispatch.TypeInfo<SystemAction>(options)),
            "none" => element.Deserialize(JsonDispatch.TypeInfo<NoneAction>(options)),
            "rewrite" => element.Deserialize(JsonDispatch.TypeInfo<RewriteAction>(options)),
            "reroute" => element.Deserialize(JsonDispatch.TypeInfo<RerouteAction>(options)),
            _ => new UnknownAction(type ?? "", element.Clone()),
        };
    }

    public override void Write(Utf8JsonWriter writer, RoutingAction value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case UnknownAction unknown:
                unknown.Raw.WriteTo(writer);
                break;
            case OpenAction a:
                JsonSerializer.Serialize(writer, a, JsonDispatch.TypeInfo<OpenAction>(options));
                break;
            case SystemAction a:
                JsonSerializer.Serialize(writer, a, JsonDispatch.TypeInfo<SystemAction>(options));
                break;
            case NoneAction a:
                JsonSerializer.Serialize(writer, a, JsonDispatch.TypeInfo<NoneAction>(options));
                break;
            case RewriteAction a:
                JsonSerializer.Serialize(writer, a, JsonDispatch.TypeInfo<RewriteAction>(options));
                break;
            case RerouteAction a:
                JsonSerializer.Serialize(writer, a, JsonDispatch.TypeInfo<RerouteAction>(options));
                break;
            default:
                throw new JsonException($"Unsupported action class: {value.GetType().Name}");
        }
    }
}
