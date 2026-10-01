using System.ComponentModel;
using System.Diagnostics;
using Preferis.Core.Hosting;
using Preferis.Core.Picker;

namespace Preferis.Platform.Launching;

/// <summary>
/// Runs the picker executable as a child process: request JSON on stdin, response JSON on stdout,
/// the picker's own log lines on stderr (docs/Design.md §9.3). Any failure yields null and the Router falls back.
/// </summary>
public sealed class ProcessPickerLauncher : IPickerLauncher
{
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;
    private readonly Action<string>? _logStderr;

    public ProcessPickerLauncher(string executablePath, TimeSpan timeout, Action<string>? logStderr)
    {
        _executablePath = executablePath;
        _timeout = timeout;
        _logStderr = logStderr;
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

            if (!process.WaitForExit((int)_timeout.TotalMilliseconds))
            {
                _logStderr?.Invoke("picker timed out; killing it");
                process.Kill(entireProcessTree: true);
                return null;
            }

            var output = stdout.GetAwaiter().GetResult();
            stderr.Wait(TimeSpan.FromSeconds(1));
            return PickerMessages.TryParseResponse(output.Trim(), request);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            _logStderr?.Invoke($"picker failed: {ex.Message}");
            return null;
        }
    }
}
