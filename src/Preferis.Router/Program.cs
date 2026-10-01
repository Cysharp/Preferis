namespace Preferis.Router;

internal static class Program
{
    private static int Main(string[] args)
    {
        // M1 ships the dry-run mode only; URL reception and launching arrive with M2 (docs/Design.md §14).
        if (args.Length >= 2 && args[0] == "--dry-run")
        {
            string? configPath = null;
            for (var i = 2; i < args.Length - 1; i++)
            {
                if (args[i] == "--config")
                {
                    configPath = args[i + 1];
                }
            }
            return DryRunCommand.Run(args[1], configPath, Console.Out);
        }

        Console.Error.WriteLine("""
            Preferis router

            Usage:
              Preferis --dry-run <url> [--config <path>]   Show how <url> would be routed, without opening anything.
            """);
        return 1;
    }
}
