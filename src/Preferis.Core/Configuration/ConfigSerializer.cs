using System.Text.Json;

namespace Preferis.Core.Configuration;

/// <summary>Reads and writes <c>config.json</c> and <c>local.json</c>.</summary>
public static class ConfigSerializer
{
    public static PreferisConfig Deserialize(string json)
        => JsonSerializer.Deserialize(json, PreferisJsonContext.Default.PreferisConfig) ?? throw new JsonException("The configuration document is empty.");

    public static PreferisConfig Deserialize(Stream json)
        => JsonSerializer.Deserialize(json, PreferisJsonContext.Default.PreferisConfig) ?? throw new JsonException("The configuration document is empty.");

    public static string Serialize(PreferisConfig config)
        => JsonSerializer.Serialize(config, PreferisJsonContext.Default.PreferisConfig);

    public static LocalConfig DeserializeLocal(string json)
        => JsonSerializer.Deserialize(json, PreferisJsonContext.Default.LocalConfig) ?? throw new JsonException("The local configuration document is empty.");

    public static string SerializeLocal(LocalConfig config)
        => JsonSerializer.Serialize(config, PreferisJsonContext.Default.LocalConfig);
}
