using System.Text.RegularExpressions;

namespace Preferis.Core.Profiles;

/// <summary>Expands environment variables and <c>~</c> in paths from the configuration, per platform convention.</summary>
public static partial class EnvironmentExpander
{
    public static string Expand(string input, Platform platform, Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        return platform == Platform.Windows ? ExpandWindows(input, getEnvironmentVariable) : ExpandUnix(input, getEnvironmentVariable, homeDirectory);
    }

    private static string ExpandWindows(string input, Func<string, string?> getEnvironmentVariable)
        => WindowsVariable().Replace(input, m => getEnvironmentVariable(m.Groups[1].Value) ?? m.Value);

    private static string ExpandUnix(string input, Func<string, string?> getEnvironmentVariable, string homeDirectory)
    {
        if (input == "~")
        {
            return homeDirectory;
        }
        if (input.StartsWith("~/", StringComparison.Ordinal))
        {
            input = homeDirectory + input[1..];
        }
        return UnixVariable().Replace(input, m =>
        {
            var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return getEnvironmentVariable(name) ?? m.Value;
        });
    }

    [GeneratedRegex("%([^%]+)%")]
    private static partial Regex WindowsVariable();

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}|\$([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex UnixVariable();
}
