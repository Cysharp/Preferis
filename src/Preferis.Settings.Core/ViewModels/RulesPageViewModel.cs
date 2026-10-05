using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Configuration;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>The rules page: the ordered rules and the fixed catch-all rule at the end (docs/Design.md §6).</summary>
public sealed partial class RulesPageViewModel : PageViewModel
{
    public RulesPageViewModel(SettingsViewModel settings)
        : base(settings)
    {
        Rules = new ObservableCollection<RuleViewModel>(settings.Config.Rules.Select(r => new RuleViewModel(settings, r)));
        CatchAll = new CatchAllViewModel(settings);
        SelectedRule = Rules.FirstOrDefault();
        IsCatchAllSelected = SelectedRule is null;
    }

    public override string Title => L("Rules");

    public ObservableCollection<RuleViewModel> Rules { get; }

    public CatchAllViewModel CatchAll { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentEditor))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateRuleCommand), nameof(DeleteRuleCommand), nameof(MoveRuleUpCommand), nameof(MoveRuleDownCommand))]
    public partial RuleViewModel? SelectedRule { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentEditor))]
    public partial bool IsCatchAllSelected { get; set; }

    /// <summary>What the right-hand side edits: a <see cref="RuleViewModel"/> or the <see cref="CatchAllViewModel"/>.</summary>
    public object? CurrentEditor => IsCatchAllSelected ? CatchAll : SelectedRule;

    private bool HasRule => SelectedRule is not null;

    private bool CanMoveUp => SelectedRule is not null && Rules.IndexOf(SelectedRule) > 0;

    private bool CanMoveDown => SelectedRule is not null && Rules.IndexOf(SelectedRule) < Rules.Count - 1;

    partial void OnSelectedRuleChanged(RuleViewModel? value)
    {
        if (value is not null)
        {
            IsCatchAllSelected = false;
        }
    }

    partial void OnIsCatchAllSelectedChanged(bool value)
    {
        if (value)
        {
            SelectedRule = null;
        }
    }

    /// <summary>Profiles may have been added, renamed or deleted on the profiles page.</summary>
    public override void Activate()
    {
        foreach (var rule in Rules)
        {
            rule.Refresh();
        }
        CatchAll.Refresh();
    }

    [RelayCommand]
    private void AddRule()
    {
        var name = L("New rule");
        var rule = new Rule
        {
            Id = IdGenerator.ForName(name, "rule", Settings.Config.Rules.Select(r => r.Id)),
            Name = name,
            Condition = new HostCondition { Match = MatchKind.Exact, Pattern = "" },
            Actions = [new OpenAction()],
        };
        Insert(rule, SelectedRule is null ? Rules.Count : Rules.IndexOf(SelectedRule) + 1);
    }

    [RelayCommand(CanExecute = nameof(HasRule))]
    private void DuplicateRule()
    {
        var source = SelectedRule!;
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(source.Model, PreferisJsonContext.Default.Rule), PreferisJsonContext.Default.Rule)!;
        copy.Id = IdGenerator.Unique(source.Id, Settings.Config.Rules.Select(r => r.Id));
        copy.Name = L("{0} copy", source.Name);
        Insert(copy, Rules.IndexOf(source) + 1);
    }

    /// <summary>
    /// Rules from a CSV of "URL or domain, profile name" lines (docs/Configuration.md §2.12), added after the existing
    /// ones. Says what was imported and what was not, line by line.
    /// </summary>
    [RelayCommand]
    private async Task ImportRules()
    {
        if (await Settings.Dialogs.PickFileAsync(L("Choose a CSV file of URLs or domains and profile names")) is not { } path)
        {
            return;
        }
        string csv;
        try
        {
            // UTF-8, with or without a byte order mark.
            csv = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Settings.Dialogs.AskAsync(L("The file could not be read."), ex.Message, [L("OK")]);
            return;
        }

        var result = RuleCsvImport.Import(Settings.Config, csv);
        foreach (var rule in result.Rules)
        {
            Settings.Config.Rules.Add(rule);
            Rules.Add(new RuleViewModel(Settings, rule));
        }
        if (result.Rules.Count > 0)
        {
            SelectedRule = Rules[^result.Rules.Count];
            Settings.MarkChanged();
        }
        var (title, message) = Report(result);
        await Settings.Dialogs.AskAsync(title, message, [L("OK")]);
    }

    private static (string Title, string Message) Report(RuleImportResult result)
    {
        const int ShownSkipped = 10;
        static string Lines(int count) => count == 1 ? L("1 line") : L("{0} lines", count);

        var parts = new List<string>();
        if (result.Rules.Count > 0)
        {
            parts.Add((result.Rules.Count == 1 ? L("Added 1 rule:") : L("Added {0} rules:", result.Rules.Count))
                + string.Concat(result.Rules.Select(r => $"\n• {r.Name} ({Lines(r.Condition is OrCondition or ? or.Conditions.Count : 1)})")));
        }
        if (result.Skipped.Count > 0)
        {
            var skipped = L("Skipped {0}:", Lines(result.Skipped.Count))
                + string.Concat(result.Skipped.Take(ShownSkipped).Select(s => "\n• " + L("Line {0}: {1}", s.Line, s.Reason)));
            if (result.Skipped.Count > ShownSkipped)
            {
                skipped += "\n• " + L("… and {0} more", result.Skipped.Count - ShownSkipped);
            }
            parts.Add(skipped);
        }
        if (parts.Count == 0)
        {
            parts.Add(L("The file has no lines to import."));
        }
        var title = result.ImportedLines == 0 ? L("No rules were imported.") : L("Imported {0}.", Lines(result.ImportedLines));
        return (title, string.Join("\n\n", parts));
    }

    [RelayCommand(CanExecute = nameof(HasRule))]
    private async Task DeleteRule()
    {
        var rule = SelectedRule!;
        if (await Settings.Dialogs.AskAsync(L("Delete “{0}”?", rule.Name), L("This rule will be deleted."), [L("Delete"), L("Cancel")]) != 0)
        {
            return;
        }

        // Reactivating the window can reload the settings while the confirmation is open.
        if (Settings.Rules != this)
        {
            return;
        }

        var index = Rules.IndexOf(rule);
        Settings.Config.Rules.Remove(rule.Model);
        Rules.Remove(rule);
        if (Rules.Count == 0)
        {
            IsCatchAllSelected = true;
        }
        else
        {
            SelectedRule = Rules[Math.Min(index, Rules.Count - 1)];
        }
        Settings.MarkChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveRuleUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveRuleDown() => Move(+1);

    [RelayCommand]
    private void SelectCatchAll() => IsCatchAllSelected = true;

    /// <summary>Moves a rule to <paramref name="index"/> (drag and drop in the list).</summary>
    public void MoveRule(RuleViewModel rule, int index)
    {
        var from = Rules.IndexOf(rule);
        var to = Math.Clamp(index, 0, Rules.Count - 1);
        if (from < 0 || from == to)
        {
            return;
        }
        Rules.Move(from, to);
        Settings.Config.Rules.RemoveAt(from);
        Settings.Config.Rules.Insert(to, rule.Model);
        MoveRuleUpCommand.NotifyCanExecuteChanged();
        MoveRuleDownCommand.NotifyCanExecuteChanged();
        Settings.MarkChanged();
    }

    internal void UpdateIssues(IReadOnlyList<ConfigIssue> issues)
    {
        foreach (var rule in Rules)
        {
            rule.Issues = issues.Where(i => i.Scope == IssueScope.Rule && i.Id == rule.Id).ToList();
        }
        CatchAll.Issues = issues.Where(i => i.Scope == IssueScope.CatchAll).ToList();
    }

    private void Insert(Rule rule, int index)
    {
        Settings.Config.Rules.Insert(index, rule);
        var viewModel = new RuleViewModel(Settings, rule);
        Rules.Insert(index, viewModel);
        SelectedRule = viewModel;
        Settings.MarkChanged();
    }

    private void Move(int delta) => MoveRule(SelectedRule!, Rules.IndexOf(SelectedRule!) + delta);
}

/// <summary>
/// Editor of one rule. Condition and action edits rebuild the corresponding part of <see cref="Model"/>. The condition
/// has no view model of its own: the condition editors of both UIs work on <see cref="ConditionPredicates"/> trees.
/// </summary>
public sealed partial class RuleViewModel : ObservableObject
{
    private readonly SettingsViewModel _settings;
    private readonly bool _ready;

    public RuleViewModel(SettingsViewModel settings, Rule model)
    {
        _settings = settings;
        Model = model;
        Name = model.Name;
        Enabled = model.Enabled;
        Actions = new ActionListViewModel(settings, model.Actions, Changed);
        _ready = true;
    }

    public Rule Model { get; }

    public string Id => Model.Id;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    public ActionListViewModel Actions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIssues))]
    public partial IReadOnlyList<ConfigIssue> Issues { get; set; } = [];

    public bool HasIssues => Issues.Count > 0;

    public string Summary => RuleSummary.Describe(Model, _settings.Config);

    partial void OnNameChanged(string value)
    {
        if (_ready)
        {
            Model.Name = value;
            Changed();
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_ready)
        {
            Model.Enabled = value;
            Changed();
        }
    }

    /// <summary>
    /// Replaces the whole condition (null: none, the rule never matches). The condition editors report the whole tree
    /// after every change, and a condition from a newer Preferis can only be replaced as a whole.
    /// </summary>
    public void SetCondition(Condition? condition)
    {
        Model.Condition = condition;
        Changed();
    }

    /// <summary>Profile names in the actions and the summary may have changed elsewhere.</summary>
    internal void Refresh()
    {
        Actions.Refresh();
        OnPropertyChanged(nameof(Summary));
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        _settings.MarkChanged();
    }
}

/// <summary>The catch-all rule: no condition, only its actions are editable (docs/Design.md §6.4).</summary>
public sealed partial class CatchAllViewModel : ObservableObject
{
    private readonly SettingsViewModel _settings;

    public CatchAllViewModel(SettingsViewModel settings)
    {
        _settings = settings;
        Actions = new ActionListViewModel(settings, settings.Config.CatchAll.Actions, Changed);
    }

    public ActionListViewModel Actions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIssues))]
    public partial IReadOnlyList<ConfigIssue> Issues { get; set; } = [];

    public bool HasIssues => Issues.Count > 0;

    public string Summary => RuleSummary.DescribeActions(_settings.Config.CatchAll.Actions, _settings.Config);

    internal void Refresh()
    {
        Actions.Refresh();
        OnPropertyChanged(nameof(Summary));
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        _settings.MarkChanged();
    }
}

/// <summary>One line descriptions of rules for the rule list.</summary>
internal static class RuleSummary
{
    public static string Describe(Rule rule, PreferisConfig config) => $"{DescribeCondition(rule.Condition)} → {DescribeActions(rule.Actions, config)}";

    public static string DescribeCondition(Condition? condition) => condition switch
    {
        null => L("no condition"),
        AndCondition and => L("all of {0}", Count(and.Conditions.Count)),
        OrCondition or => L("any of {0}", Count(or.Conditions.Count)),
        NotCondition not => L("not {0}", DescribeCondition(not.Condition)),
        HostCondition host => Comparison(L("host"), host.Match, host.Pattern),
        UrlCondition url => Comparison(L("url"), url.Match, url.Pattern),
        _ => L("{0} (unknown)", condition.Type),
    };

    public static string DescribeActions(List<RoutingAction> actions, PreferisConfig config)
        => actions.Count == 0 ? L("no action") : string.Join(L(", "), actions.Select(a => DescribeAction(a, config)));

    // A sentence for each comparison, as languages place the pattern differently.
    private static string Comparison(string subject, MatchKind match, string pattern) => match switch
    {
        MatchKind.Regex => L("{0} matches regex {1}", subject, pattern),
        MatchKind.Wildcard => L("{0} matches {1}", subject, pattern),
        MatchKind.StartsWith => L("{0} starts with {1}", subject, pattern),
        MatchKind.EndsWith => L("{0} ends with {1}", subject, pattern),
        MatchKind.Contains => L("{0} contains {1}", subject, pattern),
        _ => L("{0} is {1}", subject, pattern),
    };

    private static string DescribeAction(RoutingAction action, PreferisConfig config) => action switch
    {
        OpenAction { Profiles.Count: > 0 } open => L("open {0}", string.Join(L(", "), open.Profiles.Select(id => config.Profiles.FirstOrDefault(p => p.Id == id)?.Name ?? id))),
        OpenAction => L("open the default profile"),
        SystemAction => L("hand to the system default"),
        NoneAction => L("do nothing"),
        RewriteAction { Mode: RewriteMode.QueryParameter } rewrite => L("rewrite (query parameter {0})", rewrite.Parameter),
        RewriteAction => L("rewrite (regex)"),
        RerouteAction => L("reroute"),
        _ => L("{0} (unknown)", action.Type),
    };

    private static string Count(int count) => count == 1 ? L("1 condition") : L("{0} conditions", count);
}
