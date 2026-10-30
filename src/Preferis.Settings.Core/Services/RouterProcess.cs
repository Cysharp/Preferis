using System.ComponentModel;
using System.Diagnostics;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Services;

/// <summary>Runs the Router (<c>--register</c>, a URL to open) for the settings UI and collects what it reports.</summary>
internal static class RouterProcess
{
    // Output still in the pipe when the Router exits arrives right after; nothing worth waiting longer for.
    private static readonly TimeSpan Drain = TimeSpan.FromMilliseconds(300);

    /// <remarks>
    /// Done when the Router exits, not when its output ends: a browser it starts may inherit the output and keep it open
    /// for as long as the browser runs.
    /// </remarks>
    public static async Task<RouterRunResult> RunAsync(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var lines = new List<string>();
        var collecting = true;
        try
        {
            using var process = new Process { StartInfo = info };
            process.Start();
            // Not BeginOutputReadLine: it reads the pipes on the thread pool, and a read blocks its thread until output
            // arrives, so a browser holding the output would hold two pool threads for as long as it runs, and the Exited
            // and Drain callbacks below could wait behind them for a free one.
            foreach (var output in (StreamReader[])[process.StandardOutput, process.StandardError])
            {
                new Thread(() => Collect(output)) { IsBackground = true }.Start();
            }

            // WaitForExitAsync also waits for the end of the output; Exited does not.
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => exited.TrySetResult();
            if (process.HasExited)
            {
                exited.TrySetResult();
            }
            await exited.Task.ConfigureAwait(false);
            await Task.Delay(Drain).ConfigureAwait(false);

            lock (lines)
            {
                collecting = false;
                return new RouterRunResult(process.ExitCode, [.. lines]);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new RouterRunResult(2, [L("Could not run Preferis: {0}", ex.Message)]);
        }

        // Reads to the end even after the result is in, so that a browser writing more never fills the pipe and blocks.
        void Collect(StreamReader output)
        {
            using (output)
            {
                try
                {
                    while (output.ReadLine() is { } line)
                    {
                        lock (lines)
                        {
                            if (collecting && line.Trim().Length > 0)
                            {
                                lines.Add(line.Trim());
                            }
                        }
                    }
                }
                catch (IOException)
                {
                    // The pipe broke: there is nothing more to read.
                }
            }
        }
    }
}
