using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Configuration;

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

    public override string Title => "Rules";

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
        var name = "New rule";
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
        copy.Name = source.Name + " copy";
        Insert(copy, Rules.IndexOf(source) + 1);
    }

    [RelayCommand(CanExecute = nameof(HasRule))]
    private void DeleteRule()
    {
        var rule = SelectedRule!;
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

    private void Move(int delta)
    {
        var rule = SelectedRule!;
        var from = Rules.IndexOf(rule);
        var to = from + delta;
        Rules.Move(from, to);
        Settings.Config.Rules.RemoveAt(from);
        Settings.Config.Rules.Insert(to, rule.Model);
        MoveRuleUpCommand.NotifyCanExecuteChanged();
        MoveRuleDownCommand.NotifyCanExecuteChanged();
        Settings.MarkChanged();
    }
}

/// <summary>Editor of one rule. Condition and action edits rebuild the corresponding part of <see cref="Model"/>.</summary>
public sealed partial class RuleViewModel : ObservableObject, IConditionOwner
{
    private readonly SettingsViewModel _settings;
    private readonly bool _ready;

    public RuleViewModel(SettingsViewModel settings, Rule model)
    {
        _settings = settings;
        Model = model;
        Name = model.Name;
        Enabled = model.Enabled;
        Condition = model.Condition is null ? null : ConditionNodeViewModel.FromModel(model.Condition, this, null);
        Actions = new ActionListViewModel(settings, model.Actions, Changed);
        _ready = true;
    }

    public Rule Model { get; }

    public string Id => Model.Id;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCondition))]
    public partial ConditionNodeViewModel? Condition { get; private set; }

    public bool HasCondition => Condition is not null;

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

    [RelayCommand]
    private void AddCondition()
    {
        var leaf = new LeafConditionViewModel(this, null, new HostCondition { Match = MatchKind.Exact, Pattern = "" });
        leaf.MarkReady();
        Condition = leaf;
        ConditionChanged();
    }

    public void ConditionChanged()
    {
        if (_ready)
        {
            Model.Condition = Condition?.ToModel();
            Changed();
        }
    }

    public void ReplaceRoot(ConditionNodeViewModel? node)
    {
        if (node is not null)
        {
            node.Parent = null;
        }
        Condition = node;
        ConditionChanged();
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
        null => "no condition",
        AndCondition and => $"all of {Count(and.Conditions.Count)}",
        OrCondition or => $"any of {Count(or.Conditions.Count)}",
        NotCondition not => $"not {DescribeCondition(not.Condition)}",
        HostCondition host => $"host {Verb(host.Match)} {host.Pattern}",
        UrlCondition url => $"url {Verb(url.Match)} {url.Pattern}",
        _ => $"{condition.Type} (unknown)",
    };

    public static string DescribeActions(List<RoutingAction> actions, PreferisConfig config)
        => actions.Count == 0 ? "no action" : string.Join(", ", actions.Select(a => DescribeAction(a, config)));

    public static string Verb(MatchKind match) => match switch
    {
        MatchKind.Regex => "matches regex",
        MatchKind.Wildcard => "matches",
        MatchKind.StartsWith => "starts with",
        MatchKind.EndsWith => "ends with",
        MatchKind.Contains => "contains",
        _ => "is",
    };

    private static string DescribeAction(RoutingAction action, PreferisConfig config) => action switch
    {
        OpenAction { Profiles.Count: > 0 } open => "open " + string.Join(", ", open.Profiles.Select(id => config.Profiles.FirstOrDefault(p => p.Id == id)?.Name ?? id)),
        OpenAction => "open the default profile",
        SystemAction => "hand to the system default",
        NoneAction => "do nothing",
        RewriteAction { Mode: RewriteMode.QueryParameter } rewrite => $"rewrite (query parameter {rewrite.Parameter})",
        RewriteAction => "rewrite (regex)",
        RerouteAction => "reroute",
        _ => $"{action.Type} (unknown)",
    };

    private static string Count(int count) => count == 1 ? "1 condition" : $"{count} conditions";
}
