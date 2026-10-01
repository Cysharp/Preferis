using System.Text;

namespace Preferis.Core.Configuration;

/// <summary>Creates the human readable slugs used as profile and rule ids (docs/Configuration.md §2.3).</summary>
public static class IdGenerator
{
    private const int MaxLength = 48;

    /// <summary>Lower-case ASCII letters and digits separated by single dashes; <paramref name="fallback"/> when nothing is left.</summary>
    public static string Slugify(string name, string fallback)
    {
        var builder = new StringBuilder(Math.Min(name.Length, MaxLength));
        foreach (var ch in name)
        {
            if (builder.Length >= MaxLength)
            {
                break;
            }
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }
        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? fallback : slug;
    }

    /// <summary><paramref name="baseId"/> if it is free, otherwise the first free <c>baseId-2</c>, <c>baseId-3</c>, ...</summary>
    public static string Unique(string baseId, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(baseId))
        {
            return baseId;
        }
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseId}-{i}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    public static string ForName(string name, string fallback, IEnumerable<string> existing) => Unique(Slugify(name, fallback), existing);
}
