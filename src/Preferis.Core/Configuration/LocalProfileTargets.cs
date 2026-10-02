namespace Preferis.Core.Configuration;

/// <summary>
/// The targets this machine uses instead of the shared ones (<c>local.json</c> <c>profileTargets</c>,
/// docs/Configuration.md §3.1, docs/Design.md §7.4).
/// </summary>
public static class LocalProfileTargets
{
    /// <summary>
    /// The configuration as this machine routes with it: each profile with a local target gets it in place of the shared
    /// one. The shared configuration is not modified and is returned itself when nothing applies. Never save the result:
    /// local targets belong in <c>local.json</c>.
    /// </summary>
    public static PreferisConfig Apply(PreferisConfig config, LocalConfig local)
    {
        if (local.ProfileTargets is not { Count: > 0 })
        {
            return config;
        }

        var changed = false;
        var profiles = config.Profiles.Select(profile =>
        {
            if (Find(local, profile, config) is not { } target)
            {
                return profile;
            }
            changed = true;
            return WithTarget(profile, target);
        }).ToList();

        return !changed ? config : new PreferisConfig
        {
            Version = config.Version,
            DefaultProfile = config.DefaultProfile,
            Profiles = profiles,
            Rules = config.Rules,
            CatchAll = config.CatchAll,
            Settings = config.Settings,
            ExtensionData = config.ExtensionData,
        };
    }

    /// <summary>
    /// The local target of <paramref name="profile"/>: under its id, or else under the most recent of its previous ids.
    /// With <paramref name="config"/>, a previous id that another profile now has is not taken for this one.
    /// </summary>
    public static ProfileTarget? Find(LocalConfig local, Profile profile, PreferisConfig? config = null)
    {
        if (local.ProfileTargets is not { } targets)
        {
            return null;
        }
        if (targets.TryGetValue(profile.Id, out var target))
        {
            return target;
        }
        foreach (var previous in profile.PreviousIds ?? [])
        {
            if (targets.TryGetValue(previous, out target) && !IsCurrentIdOfAnother(config, profile, previous))
            {
                return target;
            }
        }
        return null;
    }

    /// <summary>
    /// Moves targets kept under a previous id (set before the profile was renamed on another machine) to the current id.
    /// A target already under the current id wins and the stale one is dropped. Returns whether anything changed.
    /// </summary>
    public static bool MigrateKeys(PreferisConfig config, LocalConfig local)
    {
        if (local.ProfileTargets is not { Count: > 0 } targets)
        {
            return false;
        }

        var changed = false;
        foreach (var profile in config.Profiles)
        {
            foreach (var previous in profile.PreviousIds ?? [])
            {
                if (!targets.Remove(previous, out var target) || IsCurrentIdOfAnother(config, profile, previous))
                {
                    if (target is not null)
                    {
                        targets[previous] = target;
                    }
                    continue;
                }
                changed = true;
                targets.TryAdd(profile.Id, target);
            }
        }
        return changed;
    }

    /// <summary>A local target that opens what the shared target opens on <paramref name="platform"/>, to start editing from.</summary>
    public static ProfileTarget FromShared(Profile profile, HostPlatform platform)
    {
        if (profile.Command?.For(platform) is { } command)
        {
            return new ProfileTarget { Command = Copy(command) };
        }
        return new ProfileTarget
        {
            Browser = profile.Browser?.For(platform),
            BrowserProfile = profile.BrowserProfile is { } reference ? Copy(reference) : null,
            Private = profile.Private,
            ExtraArgs = profile.ExtraArgs is { } args ? [.. args] : null,
        };
    }

    /// <summary>A deep copy, for a duplicated profile.</summary>
    public static ProfileTarget Copy(ProfileTarget target) => new()
    {
        Browser = target.Browser,
        BrowserProfile = target.BrowserProfile is { } reference ? Copy(reference) : null,
        Private = target.Private,
        ExtraArgs = target.ExtraArgs is { } args ? [.. args] : null,
        Command = target.Command is { } command ? Copy(command) : null,
        ExtensionData = target.ExtensionData is { } data ? new(data) : null,
    };

    // The profile as this machine sees it: its name and fallback, the local target. Like the shared target, a command
    // wins over a browser when a hand-written file has both.
    private static Profile WithTarget(Profile profile, ProfileTarget target) => new()
    {
        Id = profile.Id,
        PreviousIds = profile.PreviousIds,
        Name = profile.Name,
        Fallback = profile.Fallback,
        ExtensionData = profile.ExtensionData,
        Command = target.Command is { } command ? new CommandTarget { Windows = command, MacOS = command } : null,
        Browser = target.Command is null && target.Browser is { Length: > 0 } browser ? new BrowserReference(browser) : null,
        BrowserProfile = target.Command is null ? target.BrowserProfile : null,
        Private = target.Command is null && target.Private,
        ExtraArgs = target.Command is null ? target.ExtraArgs : null,
    };

    private static bool IsCurrentIdOfAnother(PreferisConfig? config, Profile profile, string id)
        => config?.Profiles.Any(p => !ReferenceEquals(p, profile) && p.Id == id) == true;

    private static BrowserProfileRef Copy(BrowserProfileRef reference) => new()
    {
        Directory = reference.Directory,
        Name = reference.Name,
        Email = reference.Email,
        ExtensionData = reference.ExtensionData is { } data ? new(data) : null,
    };

    private static CommandSpec Copy(CommandSpec spec) => new()
    {
        Path = spec.Path,
        Args = [.. spec.Args],
        ExtensionData = spec.ExtensionData is { } data ? new(data) : null,
    };
}
