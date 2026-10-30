using System.Collections.Concurrent;

namespace Preferis.Settings.Core.Tests;

/// <summary>
/// The settings window's UI thread: one thread runs the test and every continuation posted back to it, as the window's
/// dispatcher does. xUnit resumes a test on the thread pool instead, so two operations in flight at once (a save and the
/// window closing) would resume on two threads and use the view models together, which the window never does.
/// </summary>
internal sealed class UiThread : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    private UiThread()
    {
    }

    /// <summary>Runs <paramref name="test"/> on a UI thread of its own and completes with it.</summary>
    public static Task RunAsync(Func<Task> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var ui = new UiThread();
            SetSynchronizationContext(ui);
            var running = Run(test);
            // Its last step may run elsewhere (ConfigureAwait(false)); this wakes the loop to see that it is over.
            running.ContinueWith(_ => ui.Post(_ => { }, null), TaskScheduler.Default);
            while (!running.IsCompleted)
            {
                var (callback, state) = ui._queue.Take();
                callback(state);
            }
            done.SetFromTask(running);
        })
        {
            IsBackground = true,
            Name = "UI",
        };
        thread.Start();
        return done.Task;

        static async Task Run(Func<Task> test) => await test();
    }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
}
