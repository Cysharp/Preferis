using Foundation;
using Preferis.Settings.Services;

namespace Preferis.Settings.MacOS;

/// <summary>Saves after a pause in editing: every edit restarts the wait (<see cref="ISaveScheduler"/>).</summary>
internal sealed class TimerSaveScheduler : ISaveScheduler
{
    // Long enough for a word typed into a field, short enough that the file is current by the time anyone looks.
    private const double PauseSeconds = 0.6;
    private NSTimer? _timer;

    public void Schedule(Func<Task> save)
    {
        _timer?.Invalidate();
        _timer = NSTimer.CreateScheduledTimer(PauseSeconds, async _ =>
        {
            _timer = null;
            try
            {
                await save();
            }
            catch (Exception ex)
            {
                // An exception escaping a block ends the process; the edits stay unsaved and are tried again on the
                // next edit and when the window closes.
                Console.Error.WriteLine($"Saving failed: {ex}");
            }
        });
    }
}
