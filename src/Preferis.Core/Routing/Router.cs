using Preferis.Core.Configuration;
using static Preferis.Core.Localization.CoreText;

namespace Preferis.Core.Routing;

/// <summary>
/// The routing engine: evaluates rules in order and executes the first match's actions.
/// Pure: depends only on the configuration and the URL. Semantics are specified in docs/Design.md §6.1.
/// </summary>
public sealed class Router
{
    /// <summary>Maximum number of <c>reroute</c> restarts for one URL.</summary>
    public const int MaxHops = 10;

    private readonly PreferisConfig _config;
    private readonly HashSet<string> _profileIds;

    public Router(PreferisConfig config)
    {
        _config = config;
        _profileIds = config.Profiles.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
    }

    public RoutingResult Route(RoutingUrl url)
    {
        var trace = new List<TraceEntry>();
        var (decision, ruleId) = RouteCore(url, trace);
        trace.Add(new TraceEntry(TraceKind.Decision, null, Describe(decision)));
        return new RoutingResult(decision, trace) { RuleId = ruleId };
    }

    private (RoutingDecision Decision, string? RuleId) RouteCore(RoutingUrl start, List<TraceEntry> trace)
    {
        var current = start;
        var hops = 0;
        // Routing is a deterministic function of the URL, so restarting with a URL seen before can only loop.
        var seen = new HashSet<string>(StringComparer.Ordinal) { start.AbsoluteUri };

        while (true)
        {
            var outcome = EvaluateOnce(ref current, trace);
            if (outcome.Decision is { } decision)
            {
                return (decision, outcome.RuleId);
            }

            // Reroute requested.
            if (hops >= MaxHops)
            {
                return (new RoutingDecision.Failed(current, L("Reroute hop limit ({0}) exceeded.", MaxHops)), outcome.RuleId);
            }
            if (!seen.Add(current.AbsoluteUri))
            {
                return (new RoutingDecision.Failed(current, L("Reroute loop detected at '{0}'.", current.AbsoluteUri)), outcome.RuleId);
            }
            hops++;
            trace.Add(new TraceEntry(TraceKind.Rerouted, outcome.RuleId, L("Reroute #{0} with {1}", hops, current.AbsoluteUri)));
        }
    }

    /// <summary>Runs through the rules once. Returns a decision, or a reroute request (Decision == null).</summary>
    private (RoutingDecision? Decision, string? RuleId) EvaluateOnce(ref RoutingUrl current, List<TraceEntry> trace)
    {
        foreach (var rule in _config.Rules)
        {
            if (!rule.Enabled)
            {
                trace.Add(new TraceEntry(TraceKind.RuleDisabled, rule.Id, L("Disabled")));
                continue;
            }

            if (!ConditionEvaluator.Matches(rule.Condition, current))
            {
                trace.Add(new TraceEntry(TraceKind.RuleNotMatched, rule.Id, L("No match for {0}", current.AbsoluteUri)));
                continue;
            }

            trace.Add(new TraceEntry(TraceKind.RuleMatched, rule.Id, L("Matched {0}", current.AbsoluteUri)));
            var outcome = ExecuteActions(rule.Actions, rule.Id, ref current, trace);
            switch (outcome)
            {
                case ActionOutcome.Decided(var decision):
                    return (decision, rule.Id);
                case ActionOutcome.Reroute:
                    return (null, rule.Id);
                case ActionOutcome.FallThrough:
                    trace.Add(new TraceEntry(TraceKind.FellThrough, rule.Id, L("No terminal action; continuing with {0}", current.AbsoluteUri)));
                    continue;
            }
        }

        trace.Add(new TraceEntry(TraceKind.CatchAll, null, L("No rule matched; applying the catch-all rule")));
        return ExecuteActions(_config.CatchAll.Actions, null, ref current, trace) switch
        {
            ActionOutcome.Decided(var decision) => (decision, null),
            ActionOutcome.Reroute => (null, null),
            // A catch-all that does not terminate has nowhere to fall through to: open the default profile.
            _ => (Open(null, null, current, trace), null),
        };
    }

    private ActionOutcome ExecuteActions(List<RoutingAction> actions, string? ruleId, ref RoutingUrl current, List<TraceEntry> trace)
    {
        foreach (var action in actions)
        {
            trace.Add(new TraceEntry(TraceKind.Action, ruleId, ActionName(action)));
            switch (action)
            {
                case OpenAction open:
                    return new ActionOutcome.Decided(Open(open.Profiles, ruleId, current, trace));
                case SystemAction:
                    return new ActionOutcome.Decided(new RoutingDecision.System(current));
                case NoneAction:
                    return new ActionOutcome.Decided(new RoutingDecision.None(current));
                case RerouteAction:
                    return ActionOutcome.Reroute.Instance;
                case RewriteAction rewrite:
                    var (rewritten, error) = UrlRewriter.Rewrite(rewrite, current);
                    if (rewritten is null)
                    {
                        return new ActionOutcome.Decided(new RoutingDecision.Failed(current, error ?? L("Rewrite failed.")));
                    }
                    trace.Add(new TraceEntry(TraceKind.Rewritten, ruleId, $"{current.AbsoluteUri} -> {rewritten.AbsoluteUri}"));
                    current = rewritten;
                    break;
                default:
                    // An action this version does not know: refuse to guess where the URL should go.
                    return new ActionOutcome.Decided(new RoutingDecision.Failed(current, L("Unknown action type '{0}'.", action.Type)));
            }
        }

        return ActionOutcome.FallThrough.Instance;
    }

    private RoutingDecision.Open Open(List<string>? requested, string? ruleId, RoutingUrl url, List<TraceEntry> trace)
    {
        // In the order of the profile list, which the settings arrange; the action only says which profiles take part.
        var listed = (requested ?? []).ToHashSet(StringComparer.Ordinal);
        var known = _config.Profiles.Select(p => p.Id).Where(listed.Remove).ToList();
        foreach (var missing in listed)
        {
            trace.Add(new TraceEntry(TraceKind.Warning, ruleId, L("Profile '{0}' does not exist and was ignored", missing)));
        }

        if (known.Count > 0)
        {
            return new RoutingDecision.Open(url, known, OpenSource.Explicit, ruleId);
        }

        if (_config.DefaultProfile is { } defaultId && _profileIds.Contains(defaultId))
        {
            return new RoutingDecision.Open(url, [defaultId], OpenSource.DefaultProfile, ruleId);
        }

        if (_config.DefaultProfile is not null)
        {
            trace.Add(new TraceEntry(TraceKind.Warning, ruleId, L("Default profile '{0}' does not exist", _config.DefaultProfile)));
        }
        return new RoutingDecision.Open(url, _config.Profiles.Select(p => p.Id).ToList(), OpenSource.AllProfiles, ruleId);
    }

    private string Describe(RoutingDecision decision) => decision switch
    {
        RoutingDecision.Open open => L("open {0} with [{1}] ({2})", open.Url.AbsoluteUri, string.Join(", ", open.ProfileIds.Select(id => ProfileNames.Describe(_config, id))), SourceName(open.Source)),
        RoutingDecision.System system => L("system {0}", system.Url.AbsoluteUri),
        RoutingDecision.None => L("none"),
        RoutingDecision.Failed failed => L("failed: {0}", failed.Reason),
        _ => decision.ToString() ?? "",
    };

    // The type as config.json names it, in the language of the UI.
    private static string ActionName(RoutingAction action) => action switch
    {
        OpenAction => L("open"),
        SystemAction => L("system"),
        NoneAction => L("none"),
        RewriteAction => L("rewrite"),
        RerouteAction => L("reroute"),
        _ => action.Type,
    };

    private static string SourceName(OpenSource source) => source switch
    {
        OpenSource.DefaultProfile => L("DefaultProfile"),
        OpenSource.AllProfiles => L("AllProfiles"),
        _ => L("Explicit"),
    };

    private abstract record ActionOutcome
    {
        public sealed record Decided(RoutingDecision Decision) : ActionOutcome;

        public sealed record Reroute : ActionOutcome
        {
            public static readonly Reroute Instance = new();
        }

        public sealed record FallThrough : ActionOutcome
        {
            public static readonly FallThrough Instance = new();
        }
    }
}
