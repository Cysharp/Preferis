using Preferis.Core.Configuration;

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
        var decision = RouteCore(url, trace);
        trace.Add(new TraceEntry(TraceKind.Decision, null, Describe(decision)));
        return new RoutingResult(decision, trace);
    }

    private RoutingDecision RouteCore(RoutingUrl start, List<TraceEntry> trace)
    {
        var current = start;
        var hops = 0;
        // Routing is a deterministic function of the URL, so restarting with a URL seen before can only loop.
        var seen = new HashSet<string>(StringComparer.Ordinal) { start.AbsoluteUri };

        while (true)
        {
            var outcome = EvaluateOnce(ref current, trace);
            if (outcome.Decision is not null)
            {
                return outcome.Decision;
            }

            // Reroute requested.
            if (hops >= MaxHops)
            {
                return new RoutingDecision.Failed(current, $"Reroute hop limit ({MaxHops}) exceeded.");
            }
            if (!seen.Add(current.AbsoluteUri))
            {
                return new RoutingDecision.Failed(current, $"Reroute loop detected at '{current.AbsoluteUri}'.");
            }
            hops++;
            trace.Add(new TraceEntry(TraceKind.Rerouted, outcome.RuleId, $"Reroute #{hops} with {current.AbsoluteUri}"));
        }
    }

    /// <summary>Runs through the rules once. Returns a decision, or a reroute request (Decision == null).</summary>
    private (RoutingDecision? Decision, string? RuleId) EvaluateOnce(ref RoutingUrl current, List<TraceEntry> trace)
    {
        foreach (var rule in _config.Rules)
        {
            if (!rule.Enabled)
            {
                trace.Add(new TraceEntry(TraceKind.RuleDisabled, rule.Id, "Disabled"));
                continue;
            }

            if (!ConditionEvaluator.Matches(rule.Condition, current))
            {
                trace.Add(new TraceEntry(TraceKind.RuleNotMatched, rule.Id, $"No match for {current.AbsoluteUri}"));
                continue;
            }

            trace.Add(new TraceEntry(TraceKind.RuleMatched, rule.Id, $"Matched {current.AbsoluteUri}"));
            var outcome = ExecuteActions(rule.Actions, rule.Id, ref current, trace);
            switch (outcome)
            {
                case ActionOutcome.Decided(var decision):
                    return (decision, rule.Id);
                case ActionOutcome.Reroute:
                    return (null, rule.Id);
                case ActionOutcome.FallThrough:
                    trace.Add(new TraceEntry(TraceKind.FellThrough, rule.Id, $"No terminal action; continuing with {current.AbsoluteUri}"));
                    continue;
            }
        }

        trace.Add(new TraceEntry(TraceKind.CatchAll, null, "No rule matched; applying the catch-all rule"));
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
            trace.Add(new TraceEntry(TraceKind.Action, ruleId, action.Type));
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
                        return new ActionOutcome.Decided(new RoutingDecision.Failed(current, error ?? "Rewrite failed."));
                    }
                    trace.Add(new TraceEntry(TraceKind.Rewritten, ruleId, $"{current.AbsoluteUri} -> {rewritten.AbsoluteUri}"));
                    current = rewritten;
                    break;
                default:
                    // An action this version does not know: refuse to guess where the URL should go.
                    return new ActionOutcome.Decided(new RoutingDecision.Failed(current, $"Unknown action type '{action.Type}'."));
            }
        }

        return ActionOutcome.FallThrough.Instance;
    }

    private RoutingDecision.Open Open(List<string>? requested, string? ruleId, RoutingUrl url, List<TraceEntry> trace)
    {
        var known = (requested ?? []).Where(_profileIds.Contains).ToList();
        foreach (var missing in (requested ?? []).Except(known))
        {
            trace.Add(new TraceEntry(TraceKind.Warning, ruleId, $"Profile '{missing}' does not exist and was ignored"));
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
            trace.Add(new TraceEntry(TraceKind.Warning, ruleId, $"Default profile '{_config.DefaultProfile}' does not exist"));
        }
        return new RoutingDecision.Open(url, _config.Profiles.Select(p => p.Id).ToList(), OpenSource.AllProfiles, ruleId);
    }

    private static string Describe(RoutingDecision decision) => decision switch
    {
        RoutingDecision.Open open => $"open {open.Url.AbsoluteUri} with [{string.Join(", ", open.ProfileIds)}] ({open.Source})",
        RoutingDecision.System system => $"system {system.Url.AbsoluteUri}",
        RoutingDecision.None => "none",
        RoutingDecision.Failed failed => $"failed: {failed.Reason}",
        _ => decision.ToString() ?? "",
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
