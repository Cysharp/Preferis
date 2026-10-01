using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Preferis.Core.Configuration.Json;

/// <summary>
/// Dispatches on the <c>type</c> property to the concrete <see cref="Condition"/> class. Done by hand instead of
/// <see cref="JsonPolymorphicAttribute"/> so that unknown types become <see cref="UnknownCondition"/> instead of
/// failing the whole load, and so that <c>type</c> may appear anywhere in the object.
/// </summary>
internal sealed class ConditionJsonConverter : JsonConverter<Condition>
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(Condition);

    public override Condition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A condition must be a JSON object.");
        }

        var type = JsonDispatch.ReadType(element);
        return type switch
        {
            "and" => element.Deserialize(JsonDispatch.TypeInfo<AndCondition>(options)),
            "or" => element.Deserialize(JsonDispatch.TypeInfo<OrCondition>(options)),
            "not" => element.Deserialize(JsonDispatch.TypeInfo<NotCondition>(options)),
            "host" => element.Deserialize(JsonDispatch.TypeInfo<HostCondition>(options)),
            "url" => element.Deserialize(JsonDispatch.TypeInfo<UrlCondition>(options)),
            _ => new UnknownCondition(type ?? "", element.Clone()),
        };
    }

    public override void Write(Utf8JsonWriter writer, Condition value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case UnknownCondition unknown:
                unknown.Raw.WriteTo(writer);
                break;
            case AndCondition c:
                JsonSerializer.Serialize(writer, c, JsonDispatch.TypeInfo<AndCondition>(options));
                break;
            case OrCondition c:
                JsonSerializer.Serialize(writer, c, JsonDispatch.TypeInfo<OrCondition>(options));
                break;
            case NotCondition c:
                JsonSerializer.Serialize(writer, c, JsonDispatch.TypeInfo<NotCondition>(options));
                break;
            case HostCondition c:
                JsonSerializer.Serialize(writer, c, JsonDispatch.TypeInfo<HostCondition>(options));
                break;
            case UrlCondition c:
                JsonSerializer.Serialize(writer, c, JsonDispatch.TypeInfo<UrlCondition>(options));
                break;
            default:
                throw new JsonException($"Unsupported condition class: {value.GetType().Name}");
        }
    }
}

internal static class JsonDispatch
{
    public static string? ReadType(JsonElement element)
        => element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null;

    public static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions options)
        => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
