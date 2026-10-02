namespace Preferis.Platform.Windows;

/// <summary>The raw Windows command line, for what the C runtime's argument splitting must not touch.</summary>
public static class CommandLine
{
    /// <summary>Marks the rest of the command line as one URL (the registered <c>"Preferis.exe" --single-argument %1</c>).</summary>
    public const string SingleArgumentMarker = "--single-argument";

    /// <summary>
    /// The URL after <see cref="SingleArgumentMarker"/>, taken verbatim from the raw command line: quotes and spaces in it
    /// stay part of the URL instead of splitting it into options. Null when the marker or the URL is missing.
    /// </summary>
    public static string? SingleArgument(string commandLine)
    {
        var rest = SkipProgram(commandLine).TrimStart();
        if (!rest.StartsWith(SingleArgumentMarker, StringComparison.Ordinal))
        {
            return null;
        }
        rest = rest[SingleArgumentMarker.Length..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
        {
            return null;
        }
        rest = rest.Trim();
        // The shell quotes nothing after the marker, but a hand-written "%1" would.
        if (rest.Length >= 2 && rest[0] == '"' && rest[^1] == '"' && rest.IndexOf('"', 1) == rest.Length - 1)
        {
            rest = rest[1..^1];
        }
        return rest.Length == 0 ? null : rest;
    }

    // The program name ends at the closing quote when quoted, otherwise at the first white space.
    private static string SkipProgram(string commandLine)
    {
        if (commandLine.StartsWith('"'))
        {
            var end = commandLine.IndexOf('"', 1);
            return end < 0 ? "" : commandLine[(end + 1)..];
        }
        var space = commandLine.IndexOfAny([' ', '\t']);
        return space < 0 ? "" : commandLine[space..];
    }
}
