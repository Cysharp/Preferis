using Preferis.Core.Hosting;

namespace Preferis.Router;

/// <summary>
/// Appends to <c>router.log</c> in the local log directory. Off by default except for errors; the level comes from
/// <c>local.json</c> and <c>PREFERIS_TRACE=1</c> forces everything. Errors in logging itself are swallowed.
/// </summary>
internal sealed class FileLog : IRoutingLog
{
    private readonly string _path;
    private readonly LogLevel _threshold;
    private readonly bool _echoToStderr;
    private readonly object _gate = new();

    public FileLog(string directory, string? configuredLevel, bool echoToStderr)
    {
        _path = Path.Combine(directory, "router.log");
        _echoToStderr = echoToStderr;
        _threshold = Environment.GetEnvironmentVariable("PREFERIS_TRACE") == "1" ? LogLevel.Trace : configuredLevel switch
        {
            "off" => LogLevel.Error + 1,
            "trace" => LogLevel.Trace,
            "info" => LogLevel.Info,
            _ => LogLevel.Error,
        };
    }

    public void Write(LogLevel level, string message)
    {
        if (_echoToStderr && level >= LogLevel.Info)
        {
            Console.Error.WriteLine($"[{level}] {message}");
        }
        if (level < _threshold)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-7}] {message}{Environment.NewLine}";
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never break routing.
        }
    }
}
