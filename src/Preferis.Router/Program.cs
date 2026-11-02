using System.Reflection;
using Preferis.Platform.MacOS;
using Preferis.Platform.Windows;

namespace Preferis.Router;

internal static class Program
{
    private static int Main(string[] args)
    {
        // First of all: the window the link was clicked in may lose the front to another at any time (docs/Platform.md §2.9).
        var sourceApp = OperatingSystem.IsWindows() ? WindowsSourceAppDetector.CaptureForeground() : null;

        // The installer's hooks (--veloapp-install <version> and the like, docs/Platform.md §2.1) end here. A downloaded
        // update is installed by the settings, never on a link click.
        VelopackInstallation.Start(applyPendingUpdate: false);

        // Launch Services passes no arguments; URLs arrive as Apple Events (docs/Design.md §5.3).
        var launchedByLaunchServices = OperatingSystem.IsMacOS() && MacUrlActivationSource.IsLaunchedByLaunchServices;
        var urls = args.Where(a => !a.StartsWith("-psn_", StringComparison.Ordinal)).ToList();

        if (OperatingSystem.IsWindows())
        {
            // The registered command: the URL is the rest of the raw command line, so that quotes in it cannot add
            // options (docs/Platform.md §2.2). Nothing is printed, so there is no console to attach to.
            if (urls.FirstOrDefault() == CommandLine.SingleArgumentMarker)
            {
                if (CommandLine.SingleArgument(Environment.CommandLine) is not { } url)
                {
                    return 1;
                }
                var host = RouterHost.Create(interactive: false);
                return host.HandleUrls([url], host.DetectSourceApp(sourceApp));
            }
            WindowsProcess.AttachToParentConsole();
        }

        if (urls.Count == 0)
        {
            if (OperatingSystem.IsMacOS() && launchedByLaunchServices)
            {
                return RouterHost.Create(interactive: false).RunLaunchServicesLoop();
            }
            if (OperatingSystem.IsWindows())
            {
                // Opened from Explorer or the Start menu's browser entry: the settings are what the user is after.
                return RouterHost.OpenSettings(RouterHost.Create(interactive: true).Log) ? 0 : 1;
            }
            return Usage();
        }

        switch (urls[0])
        {
            case "--dry-run" when urls.Count >= 2:
                return DryRunCommand.Run(urls[1], OptionValue(urls, "--config"), SourceAppOption(urls), Console.Out);
            case "--register":
                return Report(RouterHost.Create(interactive: true).Registrar.Register());
            case "--unregister":
                return Report(RouterHost.Create(interactive: true).Registrar.Unregister());
            case "--status":
                return Status(RouterHost.Create(interactive: true));
            case "--settings":
                return RouterHost.OpenSettings(RouterHost.Create(interactive: true).Log) ? 0 : 1;
            case "--version":
                Console.WriteLine(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");
                return 0;
            case "--help" or "-h":
                return Usage();
            case var option when option.StartsWith("--", StringComparison.Ordinal):
                Console.Error.WriteLine($"Unknown option: {option}");
                return Usage();
            default:
            {
                var host = RouterHost.Create(interactive: !launchedByLaunchServices);
                return host.HandleUrls(urls, host.DetectSourceApp(sourceApp));
            }
        }
    }

    private static string? OptionValue(List<string> args, string name)
    {
        var index = args.IndexOf(name);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private static Core.Routing.SourceApp? SourceAppOption(List<string> args)
    {
        var id = OptionValue(args, "--app-id");
        var path = OptionValue(args, "--app-path");
        return id is null && path is null ? null : new Core.Routing.SourceApp(id, path);
    }

    private static int Report(Core.Hosting.RegistrationResult result)
    {
        Console.WriteLine(result.Message);
        return result.Succeeded ? 0 : 1;
    }

    private static int Status(RouterHost host)
    {
        var status = host.Registrar.GetStatus();
        var loaded = host.Config.Load();
        Console.WriteLine($"Registered with the OS: {(status.IsRegistered ? "yes" : "no")}");
        Console.WriteLine($"Default browser:        {(status.IsDefault ? "Preferis" : status.CurrentDefault ?? "unknown")}");
        Console.WriteLine($"Config:                 {loaded.ConfigPath}{(loaded.Exists ? "" : " (not found; using defaults)")}");
        Console.WriteLine($"Profiles / rules:       {loaded.Config.Profiles.Count} / {loaded.Config.Rules.Count}");
        if (loaded.Error is not null)
        {
            Console.WriteLine($"Warning:                {loaded.Error}");
        }
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine($"""
            Preferis router

            Usage:
              Preferis <url> [<url> ...]                     Route the URLs and open them. A preferis:<url> or
                                                             preferis://open?url=<encoded url> routes the URL in it.
              Preferis --dry-run <url> [--config <path>]     Show how <url> would be routed, without opening anything.
                       [--app-id <id>] [--app-path <path>]   As if the app with that id or path had opened it.
              Preferis --register | --unregister | --status  Manage the registration as the default browser.
              Preferis --settings                            Open the settings.
              Preferis --version | --help

            {(OperatingSystem.IsWindows()
                ? "Windows starts Preferis with --single-argument <url> as the default browser. Without arguments it opens the settings."
                : "When started by Launch Services (as the default browser) Preferis receives URLs from the OS instead.")}
            """);
        return 1;
    }
}
