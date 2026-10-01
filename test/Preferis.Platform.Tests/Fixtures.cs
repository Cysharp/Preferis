namespace Preferis.Platform.Tests;

internal static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static Stream Open(string name) => File.OpenRead(Path(name));
}
