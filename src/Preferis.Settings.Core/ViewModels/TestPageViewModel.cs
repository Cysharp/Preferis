using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Configuration;
using Preferis.Core.History;
using Preferis.Core.Profiles;
using Preferis.Core.Routing;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <param name="Kind">"matched", "not matched", "rewritten" ...</param>
/// <param name="Rule">Name of the rule the step belongs to; null for the catch-all and the decision.</param>
public sealed record TraceLine(string Kind, string? Rule, string Message);

/// <param name="IsResolved">The profile itself would launch (not a fallback, not a failure).</param>
/// <param name="Detail">The command that would run, or why it would not.</param>
public sealed record CandidateLine(string Name, bool IsResolved, string Detail);

/// <param name="AppId">The app that opened the URL, when the Router could tell (docs/Design.md §6.2).</param>
public sealed record HistoryLine(string Time, string Url, string Rule, string Outcome, string? AppId = null, string? AppPath = null)
{
    /// <summary>The app as the list names it: its executable or bundle, else its ID; null when unknown.</summary>
    public string? App => AppPath?.TrimEnd('\\', '/') is { Length: > 0 } path ? path[(path.LastIndexOfAny(['\\', '/']) + 1)..] : AppId;
}

/// <summary>
/// The test page: routes a URL with the configuration being edited (unsaved changes included) and shows each step,
/// the decision and how every candidate resolves on this machine, without opening anything. Same engine as
/// <c>Preferis --dry-run</c> (docs/Design.md §9.1).
/// </summary>
public sealed partial class TestPageViewModel : PageViewModel
{
    private static string ThisComputer => L("With the target of this computer.");

    public TestPageViewModel(SettingsViewModel settings)
        : base(settings)
    {
    }

    public override string Title => L("Test");

    [ObservableProperty]
    public partial string Url { get; set; } = "https://";

    /// <summary>The ID of the app to test the URL as opened by (docs/Design.md §6.2); empty when none is given.</summary>
    [ObservableProperty]
    public partial string AppId { get; set; } = "";

    /// <summary>The path of the app to test the URL as opened by; empty when none is given.</summary>
    [ObservableProperty]
    public partial string AppPath { get; set; } = "";

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial string? Decision { get; private set; }

    /// <summary>The URL after rewrites, when it differs from the input.</summary>
    [ObservableProperty]
    public partial string? RoutedUrl { get; private set; }

    public bool HasResult => Decision is not null;

    [ObservableProperty]
    public partial IReadOnlyList<TraceLine> Trace { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<CandidateLine> Candidates { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial bool IsOpening { get; private set; }

    /// <summary>What happened when the URL was opened for real, or null before that.</summary>
    [ObservableProperty]
    public partial string? OpenResult { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> OpenOutput { get; private set; } = [];

    public bool CanOpenUrls => Settings.Platform.CanOpenUrls;

    [ObservableProperty]
    public partial bool IsHistoryEnabled { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<HistoryLine> History { get; private set; } = [];

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
        Decision = null;
        RoutedUrl = null;
        Error = null;

        if (!RoutingUrl.TryParse(Url.Trim(), out var url))
        {
            Trace = [];
            Candidates = [];
            Error = L("Enter an absolute URL, such as https://www.example.com/.");
            return;
        }

        // As the Router on this machine sees it: with this machine's own targets.
        var config = LocalProfileTargets.Apply(Settings.Config, Settings.Local);
        var result = new Router(config).Route(url, Source());
        Trace = result.Trace.Select(entry => new TraceLine(
            KindText(entry.Kind), entry.RuleId is null ? null : RuleName(config, entry.RuleId), entry.Message)).ToArray();

        var source = result.RuleId is null ? L("(catch-all)") : L("(rule \"{0}\")", RuleName(config, result.RuleId));
        Decision = Describe(result.Decision, config, source);
        RoutedUrl = result.Decision.Url.AbsoluteUri != url.AbsoluteUri ? result.Decision.Url.AbsoluteUri : null;

        var candidates = new List<CandidateLine>();
        if (result.Decision is RoutingDecision.Open open)
        {
            var platform = Settings.Platform;
            var resolver = new ProfileResolver(platform.Platform, platform.BrowserLocator, platform.BrowserProfiles, platform.GetEnvironmentVariable, platform.HomeDirectory);
            foreach (var id in open.ProfileIds)
            {
                var isLocal = Settings.Config.Profiles.FirstOrDefault(p => p.Id == id) is { } shared && LocalProfileTargets.Find(Settings.Local, shared, Settings.Config) is not null;
                candidates.Add(Resolve(resolver, config, id, open.Url, isLocal));
            }
        }
        Candidates = candidates;
    }

    /// <summary>
    /// Opens the URL for real, as a clicked link would, so the picker and the launch can be tried. The Router reads the
    /// file, so edits still waiting are saved first; when they cannot be, opening with the file as it is is a choice.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task Open()
    {
        Run();
        if (Error is not null)
        {
            return;
        }

        if (Settings.HasPendingChanges && !await Settings.SaveNowAsync())
        {
            var answer = await Settings.Dialogs.AskAsync(
                L("Your latest changes are not saved."),
                L("Preferis opens the URL with the configuration on disk, which does not have them. Only this page's test uses them."),
                [L("Open Saved Configuration"), L("Cancel")]);
            if (answer != 0)
            {
                return;
            }
        }

        IsOpening = true;
        OpenResult = L("Opening…");
        OpenOutput = [];
        try
        {
            var result = await Settings.Platform.OpenUrlAsync(Url.Trim());
            OpenResult = result.ExitCode == 0 ? L("Preferis handled the URL.") : L("Preferis could not open the URL.");
            OpenOutput = result.Output;
        }
        finally
        {
            IsOpening = false;
        }
        // "Always open ... with this choice" in the picker writes config.json; show it right away.
        Settings.CheckForExternalChanges();
        LoadHistory();
    }

    private bool CanOpen => Settings.Platform.CanOpenUrls && !IsOpening;

    private SourceApp? Source()
    {
        var id = AppId.Trim();
        var path = AppPath.Trim();
        return id.Length == 0 && path.Length == 0 ? null : new SourceApp(id.Length == 0 ? null : id, path.Length == 0 ? null : path);
    }

    [RelayCommand]
    private void TestHistoryEntry(HistoryLine entry)
    {
        Url = entry.Url;
        AppId = entry.AppId ?? "";
        AppPath = entry.AppPath ?? "";
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
            Settings.StatusMessage = L("Could not clear the history: {0}", ex.Message);
        }
        History = [];
    }

    private void LoadHistory()
    {
        // The checkbox on the general page may not be saved yet; follow what is being edited.
        IsHistoryEnabled = Settings.Local.Logging?.History == true;
        if (!IsHistoryEnabled)
        {
            History = [];
            return;
        }
        try
        {
            History = RoutingHistory.Read().Select(entry => new HistoryLine(
                    entry.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    entry.Url,
                    entry.RuleId is null ? L("catch-all") : RuleName(Settings.Config, entry.RuleId),
                    OutcomeText(entry.Outcome),
                    entry.AppId,
                    entry.AppPath)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            History = [];
            Settings.StatusMessage = L("Could not read the history: {0}", ex.Message);
        }
    }

    private static CandidateLine Resolve(ProfileResolver resolver, PreferisConfig config, string id, RoutingUrl url, bool isLocal)
    {
        string NameOf(string profileId) => config.Profiles.FirstOrDefault(p => p.Id == profileId)?.Name ?? profileId;

        if (config.Profiles.FirstOrDefault(p => p.Id == id) is not { } profile)
        {
            return new CandidateLine(id, false, L("Profile '{0}' does not exist.", id));
        }

        // Resolve the profile on its own first, so a failure names this profile's problem rather than the last fallback's.
        var direct = resolver.Resolve(profile, url);
        if (direct is ResolveResult.Resolved resolved)
        {
            var command = CommandLine(resolved.Plan);
            var notes = isLocal ? resolved.Warnings.Prepend(ThisComputer).ToList() : resolved.Warnings.ToList();
            return new CandidateLine(profile.Name, true, notes.Count == 0 ? command : $"{command}  ({string.Join(" ", notes)})");
        }

        var reason = direct is ResolveResult.Unresolved unresolved ? unresolved.Reason : L("Unexpected resolution result.");
        if (isLocal)
        {
            reason = $"{ThisComputer} {reason}";
        }
        if (profile.Fallback is not { } fallbackId)
        {
            return new CandidateLine(profile.Name, false, reason);
        }
        return resolver.ResolveWithFallback(config, id, url) switch
        {
            ResolveResult.Resolved viaFallback => new CandidateLine(profile.Name, false, L("{0} Uses the fallback “{1}”: {2}", reason, NameOf(viaFallback.ProfileId), CommandLine(viaFallback.Plan))),
            ResolveResult.Unresolved last => new CandidateLine(profile.Name, false, L("{0} The fallback “{1}” cannot be used either: {2}", reason, NameOf(fallbackId), last.Reason)),
            _ => new CandidateLine(profile.Name, false, reason),
        };
    }

    private static string Describe(RoutingDecision decision, PreferisConfig config, string source) => decision switch
    {
        RoutingDecision.Open { ProfileIds.Count: 0 } => L("No profile is configured: the Router offers the browsers detected on this computer"),
        RoutingDecision.Open { Source: OpenSource.DefaultProfile } => L("Open with the default profile {0}", source),
        RoutingDecision.Open { ProfileIds.Count: 1 } open => L("Open with {0} {1}", config.Profiles.FirstOrDefault(p => p.Id == open.ProfileIds[0])?.Name ?? open.ProfileIds[0], source),
        RoutingDecision.Open { Source: OpenSource.AllProfiles } open => L("Show the picker with all {0} profiles, as there is no default profile {1}", open.ProfileIds.Count, source),
        RoutingDecision.Open open => L("Show the picker with {0} profiles {1}", open.ProfileIds.Count, source),
        RoutingDecision.System => L("Hand to the system default handler {0}", source),
        RoutingDecision.None => L("Do nothing {0}", source),
        RoutingDecision.Failed failed => L("Routing fails: {0} The Router then falls back to the default profile or the picker.", failed.Reason),
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
        TraceKind.RuleMatched => L("matched"),
        TraceKind.RuleNotMatched => L("not matched"),
        TraceKind.RuleDisabled => L("disabled"),
        TraceKind.Action => L("action"),
        TraceKind.Rewritten => L("rewritten"),
        TraceKind.FellThrough => L("fell through"),
        TraceKind.Rerouted => L("rerouted"),
        TraceKind.Reevaluated => L("re-evaluated"),
        TraceKind.CatchAll => L("catch-all"),
        TraceKind.Warning => L("warning"),
        _ => L("decision"),
    };

    // The history keeps the outcome as the Router named it (HandleOutcome).
    private static string OutcomeText(string outcome) => outcome switch
    {
        "Launched" => L("Launched"),
        "Delegated" => L("Handed to the system"),
        "Ignored" => L("Not opened"),
        "Failed" => L("Failed"),
        _ => outcome,
    };
}
