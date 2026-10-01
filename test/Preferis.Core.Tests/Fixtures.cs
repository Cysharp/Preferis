namespace Preferis.Core.Tests;

internal static class Fixtures
{
    // Mirrors the examples in docs/Configuration.md §2.1 and §3. Keep them in sync.
    public static string ConfigSample => Read("config.sample.json");
    public static string LocalSample => Read("local.sample.json");

    private static string Read(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
