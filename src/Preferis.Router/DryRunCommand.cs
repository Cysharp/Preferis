using Preferis.Core;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;
using RoutingEngine = Preferis.Core.Routing.Router;

namespace Preferis.Router;

/// <summary>
/// <c>Preferis --dry-run &lt;url&gt;</c>: loads the configuration, routes the URL and prints the trace and the
/// decision. Shares the engine with the settings UI test page. Does not resolve or launch browsers.
/// </summary>
internal static class DryRunCommand
{
    public static int Run(string input, string? configPathOverride, TextWriter output)
    {
        if (!RoutingUrl.TryParse(input, out var url))
        {
            output.WriteLine($"Not an absolute URL or a rooted file path: {input}");
            return 1;
        }

        var loaded = ConfigLoader.ForCurrentMachine().Load(configPathOverride);
        output.WriteLine($"Config:   {loaded.ConfigPath}{(loaded.Exists ? "" : " (not found; using defaults)")}");
        if (loaded.Error is not null)
        {
            output.WriteLine($"Warning:  {loaded.Error}");
        }
        output.WriteLine($"HostPlatform: {PlatformInfo.Current}");
        output.WriteLine($"URL:      {url.AbsoluteUri}");
        output.WriteLine($"Host:     {url.Host}{(url.IdnHost != url.Host ? $" ({url.IdnHost})" : "")}");
        output.WriteLine();

        // This machine's targets in place of the shared ones, as the Router routes (docs/Design.md §7.4).
        var config = LocalProfileTargets.Apply(loaded.Config, loaded.Local);
        var result = new RoutingEngine(config).Route(url);

        output.WriteLine("Trace:");
        foreach (var entry in result.Trace)
        {
            var kind = $"[{entry.Kind}]".PadRight(16);
            output.WriteLine($"  {kind} {(entry.RuleId is null ? "" : entry.RuleId + ": ")}{entry.Message}");
        }
        output.WriteLine();

        var profiles = config.Profiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
        switch (result.Decision)
        {
            case RoutingDecision.Open open:
                var how = open.ProfileIds.Count switch
                {
                    0 => "no configured candidates (the Router falls back to detected browsers)",
                    1 => "launch directly",
                    _ => "show the picker",
                };
                output.WriteLine($"Decision: open {open.Url.AbsoluteUri}");
                output.WriteLine($"          candidates from {open.Source}{(open.RuleId is null ? " (catch-all)" : $" (rule '{open.RuleId}')")}: {how}");
                foreach (var id in open.ProfileIds)
                {
                    var profile = profiles.GetValueOrDefault(id);
                    var target = profile?.Browser?.For(PlatformInfo.Current) is { } browser ? $"browser {browser}" : profile?.Command is not null ? "command" : "?";
                    var browserProfile = profile?.BrowserProfile is { } bp ? $", browser profile {bp.Name ?? bp.Email ?? bp.Directory}" : "";
                    output.WriteLine($"          - {id}: {profile?.Name ?? "(missing)"} ({target}{browserProfile}{(profile?.Private == true ? ", private" : "")}{(profile is not null && LocalProfileTargets.Find(loaded.Local, profile, loaded.Config) is not null ? ", this computer's target" : "")})");
                }
                return 0;
            case RoutingDecision.System system:
                output.WriteLine($"Decision: hand {system.Url.AbsoluteUri} to the OS default handler");
                return 0;
            case RoutingDecision.None:
                output.WriteLine("Decision: do nothing");
                return 0;
            case RoutingDecision.Failed failed:
                output.WriteLine($"Decision: FAILED - {failed.Reason}");
                output.WriteLine("          (the Router would apply the fallback chain of docs/Design.md §8)");
                return 2;
            default:
                output.WriteLine($"Decision: {result.Decision}");
                return 2;
        }
    }
}
