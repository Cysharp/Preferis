using System.Text.Json;

namespace Preferis.Core.Configuration;

/// <summary>
/// Reads and writes <c>config.json</c> and <c>local.json</c>. Reading accepts only what Preferis can process: JSON with
/// <c>null</c> where the configuration needs a value throws <see cref="JsonException"/> as malformed JSON does, so that every
/// reader (the Router, the settings, Use Existing File) treats it as a file that could not be read (docs/Design.md §8).
/// </summary>
public static class ConfigSerializer
{
    public static PreferisConfig Deserialize(string json)
        => ConfigStructure.Check(JsonSerializer.Deserialize(json, PreferisJsonContext.Default.PreferisConfig) ?? throw new JsonException("The configuration document is empty."));

    public static PreferisConfig Deserialize(Stream json)
        => ConfigStructure.Check(JsonSerializer.Deserialize(json, PreferisJsonContext.Default.PreferisConfig) ?? throw new JsonException("The configuration document is empty."));

    public static string Serialize(PreferisConfig config)
        => JsonSerializer.Serialize(config, PreferisJsonContext.Default.PreferisConfig);

    public static LocalConfig DeserializeLocal(string json)
        => ConfigStructure.Check(JsonSerializer.Deserialize(json, PreferisJsonContext.Default.LocalConfig) ?? throw new JsonException("The local configuration document is empty."));

    public static string SerializeLocal(LocalConfig config)
        => JsonSerializer.Serialize(config, PreferisJsonContext.Default.LocalConfig);
}
