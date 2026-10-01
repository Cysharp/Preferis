using System.Runtime.Versioning;
using Preferis.Core;
using Preferis.Core.Configuration;
using Preferis.Core.Hosting;
using Preferis.Core.Routing;
using Preferis.Platform.Browsers;
using Preferis.Platform.Launching;
using Preferis.Platform.MacOS;

namespace Preferis.Router;

/// <summary>Wires the Core routing service to this machine's Platform implementations and runs the entry modes.</summary>
internal sealed class RouterHost
{
    private int _inFlight;

    private RouterHost(ConfigLoader config, RoutingService service, IDefaultBrowserRegistrar registrar, IRoutingLog log)
    {
        Config = config;
        Service = service;
        Registrar = registrar;
        Log = log;
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

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Windows support arrives in M4 (docs/Design.md §14).");
        }

        // The picker is a helper bundle next to our own (docs/Platform.md §3.1).
        var pickerPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Helpers", "Preferis Picker.app", "Contents", "MacOS", "Preferis Picker"));
        var services = new RoutingServices(
            config,
            HostPlatform.MacOS,
            new MacBrowserDetector(local.BrowserOverrides, home),
            new BrowserProfileEnumerator(),
            new LaunchPlanExecutor(HostPlatform.MacOS),
            new MacSystemOpener(),
            new ProcessPickerLauncher(pickerPath, TimeSpan.FromMinutes(5), line => log.Write(LogLevel.Trace, "picker: " + line)),
            new MacUserNotifier(),
            log,
            Environment.GetEnvironmentVariable,
            home);

        return new RouterHost(config, new RoutingService(services), new MacDefaultBrowserRegistrar(), log);
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
                // The settings UI (M3) will open here. Until then there is nothing to show.
                Log.Write(LogLevel.Info, "Launched without a URL; nothing to do.");
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
            return result.Outcome;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Write(LogLevel.Error, $"Unhandled failure while routing {url.AbsoluteUri}: {ex}");
            return HandleOutcome.Failed;
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
