using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>Receives the edits of a condition tree (the rule it belongs to).</summary>
public interface IConditionOwner
{
    /// <summary>Some node changed: rebuild the model's condition.</summary>
    void ConditionChanged();

    /// <summary>Replaces the root node (null removes the condition).</summary>
    void ReplaceRoot(ConditionNodeViewModel? node);
}

public enum GroupKind
{
    /// <summary><c>and</c></summary>
    All,

    /// <summary><c>or</c></summary>
    Any,
}

public enum ConditionTarget
{
    Host,
    Url,
}

/// <summary>An entry of a drop-down list. Non-generic so that the views can template every kind of choice by its title.</summary>
public record Choice(string Title);

public sealed record EnumChoice<T>(T Value, string Title) : Choice(Title);

/// <summary>
/// A node of the condition tree. <c>not</c> is shown as <see cref="IsNegated"/> on the node it wraps rather than as a
/// separate level: a negated group or leaf reads naturally and saves a level of nesting in the editor.
/// </summary>
public abstract partial class ConditionNodeViewModel : ObservableObject
{
    private bool _ready;

    protected ConditionNodeViewModel(IConditionOwner owner, GroupConditionViewModel? parent)
    {
        Owner = owner;
        Parent = parent;
    }

    protected IConditionOwner Owner { get; }

    public GroupConditionViewModel? Parent { get; internal set; }

    [ObservableProperty]
    public partial bool IsNegated { get; set; }

    public Condition ToModel() => IsNegated ? new NotCondition { Condition = ToModelCore() } : ToModelCore();

    protected abstract Condition ToModelCore();

    /// <summary>Builds the editor tree for a model condition. Nested negations collapse (<c>not not x</c> is <c>x</c>).</summary>
    public static ConditionNodeViewModel FromModel(Condition condition, IConditionOwner owner, GroupConditionViewModel? parent)
    {
        ConditionNodeViewModel node = condition switch
        {
            NotCondition { Condition: null } => new GroupConditionViewModel(owner, parent, GroupKind.All, []),
            NotCondition not => FromModel(not.Condition, owner, parent),
            AndCondition and => new GroupConditionViewModel(owner, parent, GroupKind.All, and.Conditions),
            OrCondition or => new GroupConditionViewModel(owner, parent, GroupKind.Any, or.Conditions),
            PatternCondition leaf => new LeafConditionViewModel(owner, parent, leaf),
            UnknownCondition unknown => new UnknownConditionViewModel(owner, parent, unknown),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition.Type, "Unsupported condition."),
        };
        if (condition is NotCondition)
        {
            node.IsNegated = !node.IsNegated;
        }
        node._ready = true;
        return node;
    }

    [RelayCommand]
    private void Remove()
    {
        if (Parent is { } parent)
        {
            parent.RemoveChild(this);
        }
        else
        {
            Owner.ReplaceRoot(null);
        }
    }

    [RelayCommand]
    private void WrapInGroup()
    {
        var parent = Parent;
        var group = new GroupConditionViewModel(Owner, parent, GroupKind.All, []);
        group.MarkReady();
        if (parent is null)
        {
            Owner.ReplaceRoot(group);
        }
        else
        {
            parent.ReplaceChild(this, group);
        }
        group.AddChild(this);
    }

    internal void MarkReady() => _ready = true;

    partial void OnIsNegatedChanged(bool value) => Changed();

    protected void Changed()
    {
        if (_ready)
        {
            Owner.ConditionChanged();
        }
    }
}

public sealed partial class GroupConditionViewModel : ConditionNodeViewModel
{
    public GroupConditionViewModel(IConditionOwner owner, GroupConditionViewModel? parent, GroupKind kind, IEnumerable<Condition> children)
        : base(owner, parent)
    {
        Kind = kind;
        Children = new ObservableCollection<ConditionNodeViewModel>(children.Select(c => FromModel(c, owner, this)));
    }

    [ObservableProperty]
    public partial GroupKind Kind { get; set; }

    public static IReadOnlyList<EnumChoice<GroupKind>> Kinds { get; } = [new(GroupKind.All, L("All of")), new(GroupKind.Any, L("Any of"))];

    public EnumChoice<GroupKind> SelectedKind
    {
        get => Kinds.First(k => k.Value == Kind);
        set => Kind = value?.Value ?? Kind;
    }

    public ObservableCollection<ConditionNodeViewModel> Children { get; }

    partial void OnKindChanged(GroupKind value)
    {
        OnPropertyChanged(nameof(SelectedKind));
        Changed();
    }

    [RelayCommand]
    private void AddCondition() => AddChild(new LeafConditionViewModel(Owner, this, new HostCondition { Match = MatchKind.Exact, Pattern = "" }));

    [RelayCommand]
    private void AddGroup()
    {
        var group = new GroupConditionViewModel(Owner, this, GroupKind.Any, [new HostCondition { Match = MatchKind.Exact, Pattern = "" }]);
        group.MarkReady();
        AddChild(group);
    }

    internal void AddChild(ConditionNodeViewModel child)
    {
        child.Parent = this;
        child.MarkReady();
        Children.Add(child);
        Changed();
    }

    internal void RemoveChild(ConditionNodeViewModel child)
    {
        Children.Remove(child);
        Changed();
    }

    internal void ReplaceChild(ConditionNodeViewModel child, ConditionNodeViewModel replacement)
    {
        var index = Children.IndexOf(child);
        replacement.Parent = this;
        Children[index] = replacement;
        Changed();
    }

    protected override Condition ToModelCore() => Kind == GroupKind.All
        ? new AndCondition { Conditions = Children.Select(c => c.ToModel()).ToList() }
        : new OrCondition { Conditions = Children.Select(c => c.ToModel()).ToList() };
}

public sealed partial class LeafConditionViewModel : ConditionNodeViewModel
{
    public LeafConditionViewModel(IConditionOwner owner, GroupConditionViewModel? parent, PatternCondition model)
        : base(owner, parent)
    {
        Target = model is UrlCondition ? ConditionTarget.Url : ConditionTarget.Host;
        Match = model.Match;
        Pattern = model.Pattern;
        IgnoreCase = model is not UrlCondition url || url.IgnoreCase;
    }

    public static IReadOnlyList<EnumChoice<ConditionTarget>> Targets { get; } = [new(ConditionTarget.Host, L("Host")), new(ConditionTarget.Url, L("URL"))];

    public static IReadOnlyList<EnumChoice<MatchKind>> Matches { get; } =
    [
        new(MatchKind.Exact, L("is")),
        new(MatchKind.Wildcard, L("matches (wildcard)")),
        new(MatchKind.StartsWith, L("starts with")),
        new(MatchKind.EndsWith, L("ends with")),
        new(MatchKind.Contains, L("contains")),
        new(MatchKind.Regex, L("matches regex")),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIgnoreCaseAvailable), nameof(SelectedTarget), nameof(Placeholder))]
    public partial ConditionTarget Target { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatternError), nameof(SelectedMatch), nameof(Placeholder))]
    public partial MatchKind Match { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatternError))]
    public partial string Pattern { get; set; } = "";

    [ObservableProperty]
    public partial bool IgnoreCase { get; set; } = true;

    public EnumChoice<ConditionTarget> SelectedTarget
    {
        get => Targets.First(t => t.Value == Target);
        set => Target = value?.Value ?? Target;
    }

    public EnumChoice<MatchKind> SelectedMatch
    {
        get => Matches.First(m => m.Value == Match);
        set => Match = value?.Value ?? Match;
    }

    /// <summary>Hosts are always compared ignoring case (docs/Design.md §6.2).</summary>
    public bool IsIgnoreCaseAvailable => Target == ConditionTarget.Url;

    public string? PatternError => Pattern.Length == 0 ? null : PatternMatcher.Create(Match, Pattern, ignoreCase: true).Error;

    /// <summary>An example for the empty pattern box.</summary>
    public string Placeholder => (Target, Match) switch
    {
        (ConditionTarget.Host, MatchKind.Exact) => "login.example.com",
        (ConditionTarget.Host, MatchKind.Wildcard) => "*.example.com",
        (ConditionTarget.Host, MatchKind.StartsWith) => "www.example",
        (ConditionTarget.Host, MatchKind.EndsWith) => ".example.com",
        (ConditionTarget.Host, MatchKind.Contains) => "example",
        (ConditionTarget.Host, _) => @"^[^.]+\.example\.(org|com)$",
        (_, MatchKind.Exact) => "https://example.com/",
        (_, MatchKind.Wildcard) => "https://example.com/signin/*",
        (_, MatchKind.StartsWith) => "https://www.example.com/admin/",
        (_, MatchKind.EndsWith) => ".pdf",
        (_, MatchKind.Contains) => "/admin/",
        _ => @"^https?://[^.]+\.example\.(org|com)/",
    };

    partial void OnTargetChanged(ConditionTarget value) => Changed();

    partial void OnMatchChanged(MatchKind value) => Changed();

    partial void OnPatternChanged(string value) => Changed();

    partial void OnIgnoreCaseChanged(bool value) => Changed();

    protected override Condition ToModelCore() => Target == ConditionTarget.Host
        ? new HostCondition { Match = Match, Pattern = Pattern }
        : new UrlCondition { Match = Match, Pattern = Pattern, IgnoreCase = IgnoreCase };
}

/// <summary>A condition written by a newer Preferis. Shown read-only and saved back unchanged.</summary>
public sealed class UnknownConditionViewModel : ConditionNodeViewModel
{
    private readonly UnknownCondition _model;

    public UnknownConditionViewModel(IConditionOwner owner, GroupConditionViewModel? parent, UnknownCondition model)
        : base(owner, parent)
    {
        _model = model;
    }

    public string Type => _model.Type;

    public string Json => JsonSerializer.Serialize(_model.Raw, PreferisJsonContext.Default.JsonElement);

    protected override Condition ToModelCore() => _model;
}
