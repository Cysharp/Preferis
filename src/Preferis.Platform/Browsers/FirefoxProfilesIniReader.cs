using Preferis.Core.Browsers;

namespace Preferis.Platform.Browsers;

/// <summary>Reads Firefox's <c>profiles.ini</c>. Only <c>[ProfileN]</c> sections are profiles; install and general sections are skipped.</summary>
public static class FirefoxProfilesIniReader
{
    /// <param name="baseDirectory">Directory containing <c>profiles.ini</c>, used to resolve relative profile paths.</param>
    public static IReadOnlyList<BrowserProfileInfo> Read(TextReader reader, string? baseDirectory)
    {
        var profiles = new List<BrowserProfileInfo>();
        string? section = null;
        string? name = null, path = null;
        var relative = true;

        void Flush()
        {
            if (section is not null && IsProfileSection(section) && name is not null && path is not null)
            {
                var directory = relative && baseDirectory is not null ? Path.Combine(baseDirectory, path) : path;
                profiles.Add(new BrowserProfileInfo(directory, name, null));
            }
            name = path = null;
            relative = true;
        }

        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                section = line[1..^1];
                continue;
            }
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "Name":
                    name = value;
                    break;
                case "Path":
                    path = value;
                    break;
                case "IsRelative":
                    relative = value != "0";
                    break;
            }
        }
        Flush();
        return profiles;
    }

    private static bool IsProfileSection(string section)
        => section.StartsWith("Profile", StringComparison.Ordinal) && section.Length > 7 && section[7..].All(char.IsAsciiDigit);
}
