using System.Reflection;
using Preferis.Platform.MacOS;

namespace Preferis.Router;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Launch Services passes no arguments; URLs arrive as Apple Events (docs/Design.md §5.3).
        var launchedByLaunchServices = OperatingSystem.IsMacOS() && MacUrlActivationSource.IsLaunchedByLaunchServices;
        var urls = args.Where(a => !a.StartsWith("-psn_", StringComparison.Ordinal)).ToList();

        if (urls.Count == 0)
        {
            if (OperatingSystem.IsMacOS() && launchedByLaunchServices)
            {
                return RouterHost.Create(interactive: false).RunLaunchServicesLoop();
            }
            return Usage();
        }

        switch (urls[0])
        {
            case "--dry-run" when urls.Count >= 2:
                return DryRunCommand.Run(urls[1], OptionValue(urls, "--config"), Console.Out);
            case "--register":
                return Report(RouterHost.Create(interactive: true).Registrar.Register());
            case "--unregister":
                return Report(RouterHost.Create(interactive: true).Registrar.Unregister());
            case "--status":
                return Status(RouterHost.Create(interactive: true));
            case "--version":
                Console.WriteLine(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0");
                return 0;
            case "--help" or "-h":
                return Usage();
            case var option when option.StartsWith("--", StringComparison.Ordinal):
                Console.Error.WriteLine($"Unknown option: {option}");
                return Usage();
            default:
                return RouterHost.Create(interactive: !launchedByLaunchServices).HandleUrls(urls);
        }
    }

    private static string? OptionValue(List<string> args, string name)
    {
        var index = args.IndexOf(name);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
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
        Console.Error.WriteLine("""
            Preferis router

            Usage:
              Preferis <url> [<url> ...]                     Route the URLs and open them.
              Preferis --dry-run <url> [--config <path>]     Show how <url> would be routed, without opening anything.
              Preferis --register | --unregister | --status  Manage the registration as the default browser.
              Preferis --version | --help

            When started by Launch Services (as the default browser) Preferis receives URLs from the OS instead.
            """);
        return 1;
    }
}
