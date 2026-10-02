namespace Preferis.Core.Configuration;

/// <summary>
/// Profile names: what people tell profiles apart by in the picker and the settings, so they are unique
/// (docs/Configuration.md §2.3), and what logs show next to the ids, which are only numbers by default (§2.10).
/// </summary>
public static class ProfileNames
{
    /// <summary>Whether two names would look the same in a list: surrounding spaces and case do not count. Empty names never match.</summary>
    public static bool Same(string a, string b)
    {
        var x = a.Trim();
        return x.Length > 0 && string.Equals(x, b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary><paramref name="name"/> if no other profile has it, otherwise the first free <c>name 2</c>, <c>name 3</c>, ...</summary>
    public static string Unique(string name, IEnumerable<string> existing)
    {
        var taken = existing.ToList();
        if (!taken.Any(t => Same(t, name)))
        {
            return name;
        }
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} {i}";
            if (!taken.Any(t => Same(t, candidate)))
            {
                return candidate;
            }
        }
    }

    /// <summary>A profile for logs: <c>'Chrome (Work)' (profile-3)</c>, or just the id when it has no name or does not exist.</summary>
    public static string Describe(PreferisConfig config, string profileId)
        => config.Profiles.FirstOrDefault(p => p.Id == profileId) is { } profile ? Describe(profile) : $"'{profileId}'";

    public static string Describe(Profile profile)
        => profile.Name.Trim().Length == 0 ? $"'{profile.Id}'" : $"'{profile.Name}' ({profile.Id})";
}
