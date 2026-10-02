using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Configuration;

/// <summary>Changing a profile's id and everything that refers to it (docs/Configuration.md §2.11).</summary>
public static class ProfileIds
{
    private const int MaxLength = 48;

    /// <summary>
    /// Every id a new profile must not get: the current ones, and the previous ones that other machines may still keep
    /// local targets under.
    /// </summary>
    public static IEnumerable<string> Taken(PreferisConfig config)
        => config.Profiles.SelectMany(p => (p.PreviousIds ?? []).Prepend(p.Id));

    /// <summary>Why <paramref name="newId"/> cannot replace <paramref name="currentId"/>, or null when it can (or nothing changes).</summary>
    public static string? Validate(PreferisConfig config, string currentId, string newId)
    {
        if (newId == currentId)
        {
            return null;
        }
        if (newId.Length == 0)
        {
            return L("The ID cannot be empty.");
        }
        if (newId.Length > MaxLength)
        {
            return L("The ID can have at most {0} characters.", MaxLength);
        }
        // ":" is out: the picker names detected browsers "browser:<id>".
        if (!newId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            return L("Use only ASCII letters, digits, “-”, “_” and “.”.");
        }
        if (config.Profiles.FirstOrDefault(p => p.Id == newId) is { } owner)
        {
            return L("“{0}” is already the ID of “{1}”.", newId, owner.Name);
        }
        if (config.Profiles.FirstOrDefault(p => p.Id != currentId && p.PreviousIds?.Contains(newId) == true) is { } former)
        {
            return L("“{0}” was the ID of “{1}”, and other computers may still know it by it.", newId, former.Name);
        }
        return null;
    }

    /// <summary>
    /// Gives a profile a new id and rewrites the references to it: the default profile, fallbacks, the profiles of open
    /// actions, and this machine's local target. The old id goes first in the profile's previous ids.
    /// </summary>
    /// <remarks>Actions written by a newer Preferis are kept as they are; their content is not known.</remarks>
    public static void Rename(PreferisConfig config, LocalConfig local, string oldId, string newId)
    {
        var profile = config.Profiles.First(p => p.Id == oldId);
        profile.PreviousIds = [oldId, .. (profile.PreviousIds ?? []).Where(id => id != oldId && id != newId)];
        profile.Id = newId;

        if (config.DefaultProfile == oldId)
        {
            config.DefaultProfile = newId;
        }
        foreach (var other in config.Profiles.Where(p => p.Fallback == oldId))
        {
            other.Fallback = newId;
        }
        foreach (var open in config.Rules.SelectMany(r => r.Actions).Concat(config.CatchAll.Actions).OfType<OpenAction>())
        {
            if (open.Profiles is { } profiles)
            {
                open.Profiles = profiles.Select(id => id == oldId ? newId : id).ToList();
            }
        }

        if (local.ProfileTargets is { } targets && targets.Remove(oldId, out var target))
        {
            targets[newId] = target;
        }
    }
}
