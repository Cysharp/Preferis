using Preferis.Core.History;

namespace Preferis.Core.Tests.History;

public class RoutingHistoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("preferis-history-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string FilePath => Path.Combine(_root, "logs", "history.jsonl");

    private static HistoryEntry Entry(int i) => new()
    {
        Time = new DateTimeOffset(2026, 10, 1, 12, 0, i, TimeSpan.Zero),
        Url = $"https://example.com/{i}",
        RuleId = i % 2 == 0 ? "even" : null,
        Outcome = "Launched",
        ProfileId = "edge",
    };

    [Fact]
    public void Nothing_recorded_reads_as_empty()
    {
        Assert.Empty(new RoutingHistory(FilePath).Read());
    }

    [Fact]
    public void Entries_are_read_back_newest_first()
    {
        var history = new RoutingHistory(FilePath);

        history.Append(Entry(1), maxEntries: 10);
        history.Append(Entry(2), maxEntries: 10);

        var entries = history.Read();
        Assert.Equal(["https://example.com/2", "https://example.com/1"], entries.Select(e => e.Url));
        Assert.Equal("even", entries[0].RuleId);
        Assert.Null(entries[1].RuleId);
        Assert.Equal(Entry(2).Time, entries[0].Time);
    }

    [Fact]
    public void Only_the_most_recent_entries_are_kept()
    {
        var history = new RoutingHistory(FilePath);

        for (var i = 1; i <= 5; i++)
        {
            history.Append(Entry(i), maxEntries: 3);
        }

        Assert.Equal(["https://example.com/5", "https://example.com/4", "https://example.com/3"], history.Read().Select(e => e.Url));
        Assert.Equal(3, File.ReadAllLines(FilePath).Length);
    }

    [Fact]
    public void Damaged_lines_are_skipped()
    {
        var history = new RoutingHistory(FilePath);
        history.Append(Entry(1), maxEntries: 10);
        File.AppendAllText(FilePath, "{ broken\n");
        history.Append(Entry(2), maxEntries: 10);

        Assert.Equal(2, history.Read().Count);
    }

    [Fact]
    public void Clear_removes_everything()
    {
        var history = new RoutingHistory(FilePath);
        history.Append(Entry(1), maxEntries: 10);

        history.Clear();

        Assert.Empty(history.Read());
        Assert.False(File.Exists(FilePath));
    }
}
