using System.Runtime.Versioning;
using Preferis.Core;
using Preferis.Core.Configuration;
using Preferis.Core.History;
using Preferis.Core.Hosting;
using Preferis.Core.Routing;
using Preferis.Platform.Browsers;
using Preferis.Platform.Launching;
using Preferis.Platform.MacOS;
using Preferis.Platform.Windows;

namespace Preferis.Router;

/// <summary>Wires the Core routing service to this machine's Platform implementations and runs the entry modes.</summary>
internal sealed class RouterHost
{
    private int _inFlight;
    private readonly RoutingHistory? _history;
    // URLs of one Apple Event are routed in parallel; the history's read-modify-write must not interleave.
    private readonly Lock _historyGate = new();
    private readonly int _historySize;

    private RouterHost(ConfigLoader config, RoutingService service, IDefaultBrowserRegistrar registrar, IRoutingLog log, LocalConfig local)
    {
        Config = config;
        Service = service;
        Registrar = registrar;
        Log = log;
        if (local.Logging is { History: true } logging)
        {
            _history = new RoutingHistory(config.Paths.HistoryPath);
            _historySize = logging.HistorySize;
        }
    }

    public ConfigLoader Config { get; }

    public RoutingService Service { get; }

    public IDefaultBrowserRegistrar Registrar { get; }

    public IRoutingLog Log { get; }

    public static RouterHost Create(bool interactive)
    {
        var config = ConfigLoader.ForCurrentMachine();
        var local = config.LoadLocal();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var log = new FileLog(config.Paths.LogDirectory, local.Logging?.Level, echoToStderr: interactive);
        var pickerLog = (string line) => log.Write(LogLevel.Trace, "picker: " + line);

        RoutingServices services;
        IDefaultBrowserRegistrar registrar;
        if (OperatingSystem.IsWindows())
        {
            WindowsProcess.KeepStandardHandlesFromChildren();
            // The picker and the settings are executables next to ours (docs/Platform.md §2.1).
            var picker = new ProcessPickerLauncher(
                Path.Combine(AppContext.BaseDirectory, PickerExecutable),
                TimeSpan.FromMinutes(5),
                pickerLog,
                // The shell lets the Router come to the front, but not the picker it starts: hand the right over.
                processId =>
                {
                    if (OperatingSystem.IsWindows())
                    {
                        WindowsProcess.AllowForeground(processId);
                    }
                });
            services = new RoutingServices(
                config,
                HostPlatform.Windows,
                new WindowsBrowserDetector(local.BrowserOverrides),
                new BrowserProfileEnumerator(),
                new LaunchPlanExecutor(HostPlatform.Windows),
                new WindowsSystemOpener(WindowsUrlAssociations.Instance),
                picker,
                new WindowsUserNotifier(),
                log,
                Environment.GetEnvironmentVariable,
                home);
            registrar = new WindowsDefaultBrowserRegistrar(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Preferis.exe"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            // The picker is a helper bundle next to our own (docs/Platform.md §3.1).
            var pickerPath = Path.Combine(HelpersDirectory, "Preferis Picker.app", "Contents", "MacOS", "Preferis Picker");
            services = new RoutingServices(
                config,
                HostPlatform.MacOS,
                new MacBrowserDetector(local.BrowserOverrides, home),
                new BrowserProfileEnumerator(),
                new LaunchPlanExecutor(HostPlatform.MacOS),
                new MacSystemOpener(),
                new ProcessPickerLauncher(pickerPath, TimeSpan.FromMinutes(5), pickerLog),
                new MacUserNotifier(),
                log,
                Environment.GetEnvironmentVariable,
                home);
            registrar = MacDefaultBrowserRegistrar.ForMainBundle();
        }
        else
        {
            throw new PlatformNotSupportedException("Preferis runs on Windows and macOS.");
        }

        return new RouterHost(config, new RoutingService(services), registrar, log, local);
    }

    private const string PickerExecutable = "Preferis.Picker.exe";

    private const string SettingsExecutable = "Preferis.Settings.exe";

    /// <summary><c>Preferis.app/Contents/Helpers</c>: the picker and settings bundles (docs/Platform.md §3.1).</summary>
    private static string HelpersDirectory => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Helpers"));

    private static string SettingsAppPath => Path.Combine(HelpersDirectory, "Preferis Settings.app");

    /// <summary>Opens the settings UI. An already running instance comes to the front instead of a second one starting.</summary>
    public static bool OpenSettings(IRoutingLog log)
    {
        if (OperatingSystem.IsWindows())
        {
            var settings = Path.Combine(AppContext.BaseDirectory, SettingsExecutable);
            if (!File.Exists(settings))
            {
                log.Write(LogLevel.Error, $"The settings were not found at {settings}. Build Preferis with build/Preferis.Build.proj.");
                return false;
            }
            // The new process either shows its window or brings the running one forward; either needs the right.
            WindowsProcess.AllowAnyForeground();
            using var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(settings) { UseShellExecute = false });
            return started is not null;
        }

        if (!Directory.Exists(SettingsAppPath))
        {
            log.Write(LogLevel.Error, $"The settings app was not found at {SettingsAppPath}. Build Preferis.app with build/Preferis.Build.proj.");
            return false;
        }

        // Launch Services brings a running instance to the front.
        var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        info.ArgumentList.Add(SettingsAppPath);
        using var process = System.Diagnostics.Process.Start(info);
        process?.WaitForExit(10_000);
        return process?.ExitCode == 0;
    }

    /// <summary>Routes URLs given on the command line. Exit code 0 unless one of them failed completely.</summary>
    public int HandleUrls(IEnumerable<string> inputs)
    {
        var failed = false;
        foreach (var input in inputs)
        {
            failed |= Handle(input) == HandleOutcome.Failed;
        }
        return failed ? 2 : 0;
    }

    /// <summary>macOS: run the application loop and route every URL Launch Services delivers, then quit.</summary>
    [SupportedOSPlatform("macos")]
    public int RunLaunchServicesLoop()
    {
        Log.Write(LogLevel.Trace, "Waiting for URLs from Launch Services.");
        MacUrlActivationSource.Run(
            onUrls: urls =>
            {
                foreach (var url in urls)
                {
                    // Work off the main thread so the run loop keeps receiving events while a picker is open.
                    Interlocked.Increment(ref _inFlight);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            Handle(url);
                        }
                        finally
                        {
                            if (Interlocked.Decrement(ref _inFlight) == 0)
                            {
                                TerminateWhenIdle();
                            }
                        }
                    });
                }
            },
            onLaunchedWithoutUrls: () =>
            {
                // The user opened Preferis.app itself (Finder, Spotlight, Dock): show the settings.
                Log.Write(LogLevel.Info, "Launched without a URL; opening the settings.");
                OpenSettings(Log);
                if (Volatile.Read(ref _inFlight) == 0)
                {
                    MacUrlActivationSource.Terminate();
                }
            });
        return 0;
    }

    private HandleOutcome Handle(string input)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!RoutingUrl.TryParse(input, out var url))
        {
            Log.Write(LogLevel.Error, $"Not an absolute URL or a rooted file path: {input}");
            return HandleOutcome.Failed;
        }

        try
        {
            var result = Service.Handle(url);
            Log.Write(LogLevel.Info, $"{result.Outcome}: {result.Summary} ({System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms)");
            RecordHistory(url, result);
            return result.Outcome;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write(LogLevel.Error, $"Unhandled failure while routing {url.AbsoluteUri}: {ex}");
            return HandleOutcome.Failed;
        }
    }

    // After the launch, so the opt-in history never delays opening the URL.
    private void RecordHistory(RoutingUrl url, HandleResult result)
    {
        if (_history is null)
        {
            return;
        }
        try
        {
            lock (_historyGate)
            {
                _history.Append(
                    new HistoryEntry { Time = DateTimeOffset.Now, Url = url.AbsoluteUri, RuleId = result.RuleId, Outcome = result.Outcome.ToString(), ProfileId = result.ProfileId },
                    _historySize);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write(LogLevel.Warning, $"Could not record the history: {ex.Message}");
        }
    }

    [SupportedOSPlatform("macos")]
    private void TerminateWhenIdle()
    {
        // Several URLs opened together may arrive as separate events; wait briefly before quitting.
        new Thread(() =>
        {
            Thread.Sleep(300);
            if (Volatile.Read(ref _inFlight) == 0)
            {
                MacUrlActivationSource.Terminate();
            }
        })
        { IsBackground = true }.Start();
    }
}
