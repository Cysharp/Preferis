using System.ComponentModel;
using System.Diagnostics;
using Preferis.Core.Hosting;
using Preferis.Core.Picker;

namespace Preferis.Platform.Launching;

/// <summary>
/// Runs the picker executable as a child process: request JSON on stdin, response JSON on stdout,
/// the picker's own log lines on stderr (docs/Design.md §9.3). The response is read when stdout closes, which may be
/// before the process exits. Any failure yields null and the Router falls back.
/// </summary>
public sealed class ProcessPickerLauncher : IPickerLauncher
{
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;
    private readonly Action<string>? _logStderr;
    private readonly Action<int>? _started;

    /// <param name="started">
    /// Called with the picker's process id before the request is written. On Windows the Router hands the picker its
    /// right to come to the foreground (docs/Platform.md §2.6).
    /// </param>
    public ProcessPickerLauncher(string executablePath, TimeSpan timeout, Action<string>? logStderr, Action<int>? started = null)
    {
        _executablePath = executablePath;
        _timeout = timeout;
        _logStderr = logStderr;
        _started = started;
    }

    public PickerResponse? Show(PickerRequest request)
    {
        if (!File.Exists(_executablePath))
        {
            _logStderr?.Invoke($"picker executable not found: {_executablePath}");
            return null;
        }

        var info = new ProcessStartInfo(_executablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start returned null.");
            _started?.Invoke(process.Id);
            var stderr = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    _logStderr?.Invoke(line);
                }
            });
            var stdout = process.StandardOutput.ReadToEndAsync();

            process.StandardInput.Write(PickerMessages.Serialize(request));
            process.StandardInput.Close();

            // The answer is complete when the picker closes stdout, which it does before its closing animation:
            // the browser should not wait for the animation, nor for the process to exit.
            if (!stdout.Wait(_timeout))
            {
                _logStderr?.Invoke("picker timed out; killing it");
                process.Kill(entireProcessTree: true);
                return null;
            }

            // Log lines written before the answer are normally read by now; do not wait for the picker to finish.
            stderr.Wait(TimeSpan.FromMilliseconds(100));
            return PickerMessages.TryParseResponse(stdout.Result.Trim(), request);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            _logStderr?.Invoke($"picker failed: {ex.Message}");
            return null;
        }
    }
}
