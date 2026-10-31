using System.Text.Json;
using System.Text.Json.Serialization;

namespace Preferis.Core.History;

/// <summary>One routed URL, for tuning rules in the settings UI. See docs/Design.md §8.</summary>
public sealed class HistoryEntry
{
    public DateTimeOffset Time { get; set; }

    /// <summary>The URL as received.</summary>
    public string Url { get; set; } = "";

    /// <summary>The rule that decided, or null for the catch-all.</summary>
    public string? RuleId { get; set; }

    /// <summary><see cref="Hosting.HandleOutcome"/> as text.</summary>
    public string Outcome { get; set; } = "";

    public string? ProfileId { get; set; }

    /// <summary>The <see cref="Routing.SourceApp.Id"/> of the app that opened the URL, when known.</summary>
    public string? AppId { get; set; }

    /// <summary>The <see cref="Routing.SourceApp.Path"/> of the app that opened the URL, when known.</summary>
    public string? AppPath { get; set; }
}

/// <summary>
/// The opt-in routing history: a JSON lines file in the local log directory, trimmed to the configured size.
/// URLs are sensitive, so it is written only when <c>logging.history</c> is enabled and never synced.
/// Concurrent Routers may occasionally drop an entry when they trim at the same time; that is acceptable for a history.
/// </summary>
public sealed class RoutingHistory
{
    private readonly string _path;

    public RoutingHistory(string path)
    {
        _path = path;
    }

    public string FilePath => _path;

    public void Append(HistoryEntry entry, int maxEntries)
    {
        var lines = File.Exists(_path) ? File.ReadAllLines(_path).Where(l => l.Length > 0).ToList() : [];
        lines.Add(JsonSerializer.Serialize(entry, HistoryJsonContext.Default.HistoryEntry));
        if (lines.Count > maxEntries)
        {
            lines.RemoveRange(0, lines.Count - Math.Max(maxEntries, 0));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllLines(temp, lines);
        File.Move(temp, _path, overwrite: true);
    }

    /// <summary>Newest first. Lines that cannot be parsed are skipped.</summary>
    public IReadOnlyList<HistoryEntry> Read()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var entries = new List<HistoryEntry>();
        foreach (var line in File.ReadLines(_path))
        {
            try
            {
                if (line.Length > 0 && JsonSerializer.Deserialize(line, HistoryJsonContext.Default.HistoryEntry) is { } entry)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A partially written or foreign line; the rest of the history is still useful.
            }
        }
        entries.Reverse();
        return entries;
    }

    public void Clear()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}

// One entry per line, so unlike the configuration context this one must not indent.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HistoryEntry))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext;
