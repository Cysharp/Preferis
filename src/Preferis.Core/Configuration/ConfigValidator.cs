using Preferis.Core.Browsers;
using Preferis.Core.Routing;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Configuration;

public enum IssueSeverity
{
    /// <summary>Probably not what the user meant, but routing works.</summary>
    Warning,

    /// <summary>Routing will fail or ignore part of the configuration.</summary>
    Error,
}

/// <summary>What an issue is attached to.</summary>
public enum IssueScope
{
    Config,
    Profile,
    Rule,
    CatchAll,
}

/// <param name="Id">The profile or rule id for <see cref="IssueScope.Profile"/> / <see cref="IssueScope.Rule"/>; null otherwise.</param>
public sealed record ConfigIssue(IssueSeverity Severity, IssueScope Scope, string? Id, string Message);

/// <summary>
/// Static checks of a configuration for the settings UI. Never blocks saving: the Router copes with every
/// problem reported here (docs/Design.md §6.1, §8), the list only explains what will happen.
/// </summary>
public static class ConfigValidator
{
    public static IReadOnlyList<ConfigIssue> Validate(PreferisConfig config)
    {
        var issues = new List<ConfigIssue>();
        var profileIds = config.Profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        if (config.DefaultProfile is { } defaultProfile && !profileIds.Contains(defaultProfile))
        {
            issues.Add(new(IssueSeverity.Error, IssueScope.Config, null, L("The default profile '{0}' does not exist.", defaultProfile)));
        }

        var seenProfiles = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < config.Profiles.Count; i++)
        {
            var profile = config.Profiles[i];
            ValidateProfile(profile, profileIds, seenProfiles, issues);
            // Names are what the picker shows; two that look alike cannot be told apart there.
            if (config.Profiles.Take(i).FirstOrDefault(p => ProfileNames.Same(p.Name, profile.Name)) is { } earlier)
            {
                issues.Add(new(IssueSeverity.Error, IssueScope.Profile, profile.Id, L("Another profile is already named “{0}”.", earlier.Name.Trim())));
            }
        }

        var seenRules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in config.Rules)
        {
            void Add(IssueSeverity severity, string message) => issues.Add(new(severity, IssueScope.Rule, rule.Id, message));

            if (rule.Id.Length == 0)
            {
                Add(IssueSeverity.Error, L("Rule '{0}' has no id.", rule.Name));
            }
            else if (!seenRules.Add(rule.Id))
            {
                Add(IssueSeverity.Error, L("The rule id '{0}' is used more than once.", rule.Id));
            }

            if (rule.Condition is null)
            {
                Add(IssueSeverity.Warning, L("The rule has no condition and never matches."));
            }
            else
            {
                ValidateCondition(rule.Condition, Add);
            }

            if (rule.Actions.Count == 0)
            {
                Add(IssueSeverity.Error, L("The rule has no action."));
            }
            ValidateActions(rule.Actions, profileIds, Add);
        }

        ValidateActions(config.CatchAll.Actions, profileIds, (severity, message) => issues.Add(new(severity, IssueScope.CatchAll, null, message)));
        return issues;
    }

    private static void ValidateProfile(Profile profile, HashSet<string> profileIds, HashSet<string> seen, List<ConfigIssue> issues)
    {
        void Add(IssueSeverity severity, string message) => issues.Add(new(severity, IssueScope.Profile, profile.Id, message));

        if (profile.Id.Length == 0)
        {
            Add(IssueSeverity.Error, L("Profile '{0}' has no id.", profile.Name));
        }
        else if (!seen.Add(profile.Id))
        {
            Add(IssueSeverity.Error, L("The profile id '{0}' is used more than once.", profile.Id));
        }

        var browserIds = profile.Browser is { } browser ? new[] { browser.Default, browser.Windows, browser.MacOS }.OfType<string>().ToList() : [];
        if (profile.Command is { } command)
        {
            if (browserIds.Count > 0)
            {
                Add(IssueSeverity.Warning, L("The profile has both a browser and a command; the command is used."));
            }
            if (string.IsNullOrWhiteSpace(command.Windows?.Path) && string.IsNullOrWhiteSpace(command.MacOS?.Path))
            {
                Add(IssueSeverity.Error, L("The command has no executable path for any OS."));
            }
        }
        else if (browserIds.Count == 0)
        {
            Add(IssueSeverity.Error, L("The profile has neither a browser nor a command."));
        }

        foreach (var id in browserIds.Where(id => BrowserCatalog.Find(id) is null && !BrowserCatalog.IsPlatformSpecificId(id)))
        {
            Add(IssueSeverity.Error, L("'{0}' is not a known browser id.", id));
        }

        if (profile.Fallback is { } fallback)
        {
            if (fallback == profile.Id)
            {
                Add(IssueSeverity.Error, L("The profile falls back to itself."));
            }
            else if (!profileIds.Contains(fallback))
            {
                Add(IssueSeverity.Error, L("The fallback profile '{0}' does not exist.", fallback));
            }
        }
    }

    private static void ValidateCondition(Condition condition, Action<IssueSeverity, string> add)
    {
        switch (condition)
        {
            case AndCondition { Conditions.Count: 0 }:
                add(IssueSeverity.Warning, L("An empty \"all of\" group matches every URL."));
                break;
            case OrCondition { Conditions.Count: 0 }:
                add(IssueSeverity.Warning, L("An empty \"any of\" group never matches."));
                break;
            case AndCondition and:
                and.Conditions.ForEach(c => ValidateCondition(c, add));
                break;
            case OrCondition or:
                or.Conditions.ForEach(c => ValidateCondition(c, add));
                break;
            case NotCondition { Condition: null }:
                add(IssueSeverity.Warning, L("A \"not\" group without a condition never matches."));
                break;
            case NotCondition not:
                ValidateCondition(not.Condition, add);
                break;
            case PatternCondition leaf when leaf.Pattern.Length == 0:
                add(IssueSeverity.Warning, (leaf is UrlCondition ? L("The URL pattern is empty.") : L("The host pattern is empty.")));
                break;
            case PatternCondition leaf when PatternMatcher.Create(leaf.Match, leaf.Pattern, ignoreCase: true).Error is { } error:
                add(IssueSeverity.Error, (leaf is UrlCondition ? L("The URL pattern '{0}' is invalid: {1}", leaf.Pattern, error) : L("The host pattern '{0}' is invalid: {1}", leaf.Pattern, error)));
                break;
            case UnknownCondition unknown:
                add(IssueSeverity.Warning, L("The condition type '{0}' comes from a newer Preferis and never matches here.", unknown.Type));
                break;
        }
    }

    private static void ValidateActions(List<RoutingAction> actions, HashSet<string> profileIds, Action<IssueSeverity, string> add)
    {
        var rewritten = false;
        for (var i = 0; i < actions.Count; i++)
        {
            switch (actions[i])
            {
                case OpenAction open:
                    foreach (var missing in (open.Profiles ?? []).Where(id => !profileIds.Contains(id)))
                    {
                        add(IssueSeverity.Warning, L("The profile '{0}' does not exist and is ignored.", missing));
                    }
                    break;
                case RewriteAction { Mode: RewriteMode.Regex } rewrite:
                    if (string.IsNullOrEmpty(rewrite.Pattern))
                    {
                        add(IssueSeverity.Error, L("The rewrite has no pattern."));
                    }
                    else if (PatternMatcher.Create(MatchKind.Regex, rewrite.Pattern, ignoreCase: false).Error is { } error)
                    {
                        add(IssueSeverity.Error, L("The rewrite pattern '{0}' is invalid: {1}", rewrite.Pattern, error));
                    }
                    rewritten = true;
                    break;
                case RewriteAction rewrite:
                    if (string.IsNullOrWhiteSpace(rewrite.Parameter))
                    {
                        add(IssueSeverity.Error, L("The rewrite has no query parameter name."));
                    }
                    rewritten = true;
                    break;
                case RerouteAction when !rewritten:
                    // Routing is a function of the URL: starting over with the same URL is detected as a loop.
                    add(IssueSeverity.Error, L("Reroute without a rewrite before it starts over with the same URL and always fails as a loop."));
                    break;
                case UnknownAction unknown:
                    add(IssueSeverity.Warning, L("The action type '{0}' comes from a newer Preferis; routing fails when it is reached.", unknown.Type));
                    break;
            }

            if (actions[i].IsTerminal && i < actions.Count - 1)
            {
                add(IssueSeverity.Warning, L("The actions after '{0}' never run.", actions[i].Type));
                break;
            }
        }
    }
}
