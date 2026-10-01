namespace Preferis.Core.Tests;

/// <summary>A clock that only moves when the test says so.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_ticks);

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
