namespace Preferis.Core.Routing;

/// <summary>The outcome of routing one URL. See docs/Design.md §6.</summary>
public abstract record RoutingDecision(RoutingUrl Url)
{
    /// <summary>Open with the given candidate profiles: one launches directly, several show the picker, none means "no configured candidates".</summary>
    public sealed record Open(RoutingUrl Url, IReadOnlyList<string> ProfileIds, OpenSource Source, string? RuleId) : RoutingDecision(Url);

    /// <summary>Hand the URL to the OS default handler.</summary>
    public sealed record System(RoutingUrl Url) : RoutingDecision(Url);

    /// <summary>Do nothing.</summary>
    public sealed record None(RoutingUrl Url) : RoutingDecision(Url);

    /// <summary>Routing could not complete; the caller applies the fallback chain (docs/Design.md §8).</summary>
    public sealed record Failed(RoutingUrl Url, string Reason) : RoutingDecision(Url);
}

/// <summary>Where the candidate list of an <see cref="RoutingDecision.Open"/> came from.</summary>
public enum OpenSource
{
    /// <summary>The action named the profiles.</summary>
    Explicit,

    /// <summary>The action named none; the default profile was used.</summary>
    DefaultProfile,

    /// <summary>The action named none and there is no default profile; every configured profile is a candidate.</summary>
    AllProfiles,
}

public enum TraceKind
{
    RuleDisabled,
    RuleMatched,
    RuleNotMatched,
    Action,
    Rewritten,
    FellThrough,
    Rerouted,

    /// <summary>Before the picker for a URL its own rule rewrote, the rules above are evaluated with it (docs/Design.md §6.1).</summary>
    Reevaluated,
    CatchAll,
    Warning,
    Decision,
}

/// <summary>One step of the routing process, for <c>--dry-run</c> and the settings UI test page.</summary>
public sealed record TraceEntry(TraceKind Kind, string? RuleId, string Message);

public sealed record RoutingResult(RoutingDecision Decision, IReadOnlyList<TraceEntry> Trace)
{
    /// <summary>The rule whose actions produced <see cref="Decision"/>; null when the catch-all did.</summary>
    public string? RuleId { get; init; }
}
