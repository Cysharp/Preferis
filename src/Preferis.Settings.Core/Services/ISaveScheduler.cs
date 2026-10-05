namespace Preferis.Settings.Services;

/// <summary>
/// When edits reach the disk. The settings save themselves after a short pause in editing, so that typing a pattern
/// does not write the file (and wake a sync client) at every key. The UI provides the timer; the tests run it by hand.
/// </summary>
public interface ISaveScheduler
{
    /// <summary>Starts <paramref name="save"/> on the UI thread after a short pause, replacing a request still waiting.</summary>
    void Schedule(Func<Task> save);
}
