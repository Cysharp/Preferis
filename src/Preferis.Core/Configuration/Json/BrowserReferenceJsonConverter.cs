using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.Configuration.Json;

/// <summary><c>"browser": "edge"</c> or <c>"browser": { "windows": "edge", "macos": "safari" }</c>.</summary>
internal sealed class BrowserReferenceJsonConverter : JsonConverter<BrowserReference>
{
    public override BrowserReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return new BrowserReference(reader.GetString()!);
            case JsonTokenType.StartObject:
                var reference = new BrowserReference();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    reader.Read();
                    var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
                    {
                        reader.Skip();
                    }
                    switch (name)
                    {
                        case "windows":
                            reference.Windows = value;
                            break;
                        case "macos":
                            reference.MacOS = value;
                            break;
                    }
                }
                return reference;
            default:
                throw new JsonException("A browser reference must be a string or an object.");
        }
    }

    public override void Write(Utf8JsonWriter writer, BrowserReference value, JsonSerializerOptions options)
    {
        if (value.IsPlain)
        {
            writer.WriteStringValue(value.Default);
            return;
        }

        writer.WriteStartObject();
        if (value.Windows is not null)
        {
            writer.WriteString("windows", value.Windows);
        }
        if (value.MacOS is not null)
        {
            writer.WriteString("macos", value.MacOS);
        }
        writer.WriteEndObject();
    }
}
