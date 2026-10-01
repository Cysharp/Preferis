using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Configuration;
using Preferis.Core.History;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;

namespace Preferis.Settings.ViewModels;

/// <param name="Kind">"matched", "not matched", "rewritten" ...</param>
/// <param name="Rule">Name of the rule the step belongs to; null for the catch-all and the decision.</param>
public sealed record TraceLine(string Kind, string? Rule, string Message);

/// <param name="IsResolved">The profile itself would launch (not a fallback, not a failure).</param>
/// <param name="Detail">The command that would run, or why it would not.</param>
public sealed record CandidateLine(string Name, bool IsResolved, string Detail);

public sealed record HistoryLine(string Time, string Url, string Rule, string Outcome);

/// <summary>
/// The test page: routes a URL with the configuration being edited (unsaved changes included) and shows each step,
/// the decision and how every candidate resolves on this machine, without opening anything. Same engine as
/// <c>Preferis --dry-run</c> (docs/Design.md §9.1).
/// </summary>
public sealed partial class TestPageViewModel : PageViewModel
{
    public TestPageViewModel(SettingsViewModel settings)
        : base(settings)
    {
    }

    public override string Title => "Test";

    [ObservableProperty]
    public partial string Url { get; set; } = "https://";

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial string? Decision { get; private set; }

    /// <summary>The URL after rewrites, when it differs from the input.</summary>
    [ObservableProperty]
    public partial string? RoutedUrl { get; private set; }

    public bool HasResult => Decision is not null;

    public ObservableCollection<TraceLine> Trace { get; } = [];

    public ObservableCollection<CandidateLine> Candidates { get; } = [];

    [ObservableProperty]
    public partial bool IsHistoryEnabled { get; private set; }

    public ObservableCollection<HistoryLine> History { get; } = [];

    private RoutingHistory RoutingHistory => new(Settings.Loader.Paths.HistoryPath);

    /// <summary>Shows the latest history and tests the current URL again against the latest edits.</summary>
    public override void Activate()
    {
        LoadHistory();
        if (HasResult)
        {
            Run();
        }
    }

    [RelayCommand]
    private void Run()
    {
        Trace.Clear();
        Candidates.Clear();
        Decision = null;
        RoutedUrl = null;
        Error = null;

        if (!RoutingUrl.TryParse(Url.Trim(), out var url))
        {
            Error = "Enter an absolute URL, such as https://www.example.com/.";
            return;
        }

        var config = Settings.Config;
        var result = new Router(config).Route(url);
        foreach (var entry in result.Trace)
        {
            Trace.Add(new TraceLine(KindText(entry.Kind), entry.RuleId is null ? null : RuleName(config, entry.RuleId), entry.Message));
        }

        var source = result.RuleId is null ? "(catch-all)" : $"(rule \"{RuleName(config, result.RuleId)}\")";
        Decision = Describe(result.Decision, config, source);
        RoutedUrl = result.Decision.Url.AbsoluteUri != url.AbsoluteUri ? result.Decision.Url.AbsoluteUri : null;

        if (result.Decision is RoutingDecision.Open open)
        {
            var platform = Settings.Platform;
            var resolver = new ProfileResolver(platform.Platform, platform.BrowserLocator, platform.BrowserProfiles, platform.GetEnvironmentVariable, platform.HomeDirectory);
            foreach (var id in open.ProfileIds)
            {
                Candidates.Add(Resolve(resolver, config, id, open.Url));
            }
        }
    }

    [RelayCommand]
    private void TestHistoryEntry(HistoryLine entry)
    {
        Url = entry.Url;
        Run();
    }

    [RelayCommand]
    private void ClearHistory()
    {
        try
        {
            RoutingHistory.Clear();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Settings.StatusMessage = $"Could not clear the history: {ex.Message}";
        }
        History.Clear();
    }

    private void LoadHistory()
    {
        History.Clear();
        // The checkbox on the general page may not be saved yet; follow what is being edited.
        IsHistoryEnabled = Settings.Local.Logging?.History == true;
        if (!IsHistoryEnabled)
        {
            return;
        }
        try
        {
            foreach (var entry in RoutingHistory.Read())
            {
                History.Add(new HistoryLine(
                    entry.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    entry.Url,
                    entry.RuleId is null ? "catch-all" : RuleName(Settings.Config, entry.RuleId),
                    entry.Outcome));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Settings.StatusMessage = $"Could not read the history: {ex.Message}";
        }
    }

    private static CandidateLine Resolve(ProfileResolver resolver, PreferisConfig config, string id, RoutingUrl url)
    {
        string NameOf(string profileId) => config.Profiles.FirstOrDefault(p => p.Id == profileId)?.Name ?? profileId;

        if (config.Profiles.FirstOrDefault(p => p.Id == id) is not { } profile)
        {
            return new CandidateLine(id, false, $"Profile '{id}' does not exist.");
        }

        // Resolve the profile on its own first, so a failure names this profile's problem rather than the last fallback's.
        var direct = resolver.Resolve(profile, url);
        if (direct is ResolveResult.Resolved resolved)
        {
            var command = CommandLine(resolved.Plan);
            return new CandidateLine(profile.Name, true, resolved.Warnings.Count == 0 ? command : $"{command}  ({string.Join(" ", resolved.Warnings)})");
        }

        var reason = direct is ResolveResult.Unresolved unresolved ? unresolved.Reason : "Unexpected resolution result.";
        if (profile.Fallback is not { } fallbackId)
        {
            return new CandidateLine(profile.Name, false, reason);
        }
        return resolver.ResolveWithFallback(config, id, url) switch
        {
            ResolveResult.Resolved viaFallback => new CandidateLine(profile.Name, false, $"{reason} Uses the fallback “{NameOf(viaFallback.ProfileId)}”: {CommandLine(viaFallback.Plan)}"),
            ResolveResult.Unresolved last => new CandidateLine(profile.Name, false, $"{reason} The fallback “{NameOf(fallbackId)}” cannot be used either: {last.Reason}"),
            _ => new CandidateLine(profile.Name, false, reason),
        };
    }

    private static string Describe(RoutingDecision decision, PreferisConfig config, string source) => decision switch
    {
        RoutingDecision.Open { ProfileIds.Count: 0 } => "No profile is configured: the Router offers the browsers detected on this computer",
        RoutingDecision.Open { Source: OpenSource.DefaultProfile } => $"Open with the default profile {source}",
        RoutingDecision.Open { ProfileIds.Count: 1 } open => $"Open with {config.Profiles.FirstOrDefault(p => p.Id == open.ProfileIds[0])?.Name ?? open.ProfileIds[0]} {source}",
        RoutingDecision.Open { Source: OpenSource.AllProfiles } open => $"Show the picker with all {open.ProfileIds.Count} profiles, as there is no default profile {source}",
        RoutingDecision.Open open => $"Show the picker with {open.ProfileIds.Count} profiles {source}",
        RoutingDecision.System => $"Hand to the system default handler {source}",
        RoutingDecision.None => $"Do nothing {source}",
        RoutingDecision.Failed failed => $"Routing fails: {failed.Reason} The Router then falls back to the default profile or the picker.",
        _ => decision.ToString() ?? "",
    };

    private static string CommandLine(LaunchPlan plan) => plan.Kind == LaunchKind.OpenWithApplication
        ? $"open -a {Quote(plan.Path)} {string.Join(' ', plan.Arguments.Select(Quote))}"
        : $"{Quote(plan.Path)} {string.Join(' ', plan.Arguments.Select(Quote))}";

    private static string Quote(string value)
        => value.Contains(' ', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : value;

    private static string RuleName(PreferisConfig config, string id) => config.Rules.FirstOrDefault(r => r.Id == id)?.Name ?? id;

    private static string KindText(TraceKind kind) => kind switch
    {
        TraceKind.RuleMatched => "matched",
        TraceKind.RuleNotMatched => "not matched",
        TraceKind.RuleDisabled => "disabled",
        TraceKind.Action => "action",
        TraceKind.Rewritten => "rewritten",
        TraceKind.FellThrough => "fell through",
        TraceKind.Rerouted => "rerouted",
        TraceKind.CatchAll => "catch-all",
        TraceKind.Warning => "warning",
        _ => "decision",
    };
}
