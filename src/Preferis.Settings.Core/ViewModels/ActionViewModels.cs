using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Configuration;
using Preferis.Core.Routing;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

public enum ActionKind
{
    Open,
    System,
    None,
    Rewrite,
    Reroute,
}

/// <summary>The action list of a rule or of the catch-all. Every edit rebuilds the model list in place.</summary>
public sealed partial class ActionListViewModel : ObservableObject
{
    private readonly SettingsViewModel _settings;
    private readonly List<RoutingAction> _model;
    private readonly Action _changed;

    public ActionListViewModel(SettingsViewModel settings, List<RoutingAction> model, Action changed)
    {
        _settings = settings;
        _model = model;
        _changed = changed;
        Refresh();
    }

    public ObservableCollection<ActionViewModel> Items { get; } = [];

    public static IReadOnlyList<EnumChoice<ActionKind>> Kinds { get; } =
    [
        new(ActionKind.Open, L("Open with profiles")),
        new(ActionKind.Rewrite, L("Rewrite the URL")),
        new(ActionKind.Reroute, L("Route again from the first rule")),
        new(ActionKind.System, L("Hand to the system default")),
        new(ActionKind.None, L("Do nothing")),
    ];

    internal SettingsViewModel Settings => _settings;

    [RelayCommand]
    private void Add(ActionKind kind)
    {
        RoutingAction action = kind switch
        {
            ActionKind.Open => new OpenAction(),
            ActionKind.System => new SystemAction(),
            ActionKind.None => new NoneAction(),
            ActionKind.Rewrite => new RewriteAction { Mode = RewriteMode.Regex, Pattern = "", Replacement = "" },
            _ => new RerouteAction(),
        };
        Items.Add(Create(action));
        Changed();
    }

    /// <summary>Rebuilds the editors from the model (profiles may have changed elsewhere).</summary>
    public void Refresh()
    {
        Items.Clear();
        foreach (var action in _model)
        {
            Items.Add(Create(action));
        }
        RefreshPositions();
    }

    internal void Remove(ActionViewModel item)
    {
        Items.Remove(item);
        Changed();
    }

    internal void Move(ActionViewModel item, int delta)
    {
        var from = Items.IndexOf(item);
        var to = from + delta;
        if (to < 0 || to >= Items.Count)
        {
            return;
        }
        Items.Move(from, to);
        Changed();
    }

    internal void Changed()
    {
        _model.Clear();
        _model.AddRange(Items.Select(i => i.ToModel()));
        RefreshPositions();
        _changed();
    }

    private void RefreshPositions()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].SetPosition(i, Items.Count);
        }
    }

    private ActionViewModel Create(RoutingAction action) => action switch
    {
        OpenAction open => new OpenActionViewModel(this, open),
        RewriteAction rewrite => new RewriteActionViewModel(this, rewrite),
        SystemAction => new SimpleActionViewModel(this, action, L("Hand to the system default"), L("Lets the OS default handler open the URL (never Preferis itself).")),
        NoneAction => new SimpleActionViewModel(this, action, L("Do nothing"), L("The URL is not opened.")),
        RerouteAction => new SimpleActionViewModel(this, action, L("Route again"), L("Starts over from the first rule with the current (rewritten) URL.")),
        UnknownAction unknown => new UnknownActionViewModel(this, unknown),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action.Type, "Unsupported action."),
    };
}

public abstract partial class ActionViewModel : ObservableObject
{
    protected ActionViewModel(ActionListViewModel list, RoutingAction model)
    {
        List = list;
        ExtensionData = model.ExtensionData;
    }

    protected ActionListViewModel List { get; }

    /// <summary>
    /// What a newer Preferis added to the action (docs/Configuration.md §4). Every edit, even one elsewhere in the list,
    /// rebuilds the list from <see cref="ToModel"/>, so a <see cref="ToModel"/> that builds a new action must hand these on.
    /// </summary>
    protected Dictionary<string, JsonElement>? ExtensionData { get; }

    public abstract string Title { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand))]
    public partial int Position { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    public partial int Count { get; private set; }

    /// <summary>"1." "2." ... in front of each action: they run in this order.</summary>
    public string Number => $"{Position + 1}.";

    public abstract RoutingAction ToModel();

    internal void SetPosition(int position, int count)
    {
        Position = position;
        Count = count;
        OnPropertyChanged(nameof(Number));
    }

    [RelayCommand]
    private void Remove() => List.Remove(this);

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => List.Move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => List.Move(this, +1);

    private bool CanMoveUp => Position > 0;

    private bool CanMoveDown => Position < Count - 1;

    protected void Changed() => List.Changed();
}

/// <summary>One profile in an open action's checklist.</summary>
public sealed partial class ProfileToggleViewModel : ObservableObject
{
    private readonly OpenActionViewModel _owner;

    public ProfileToggleViewModel(OpenActionViewModel owner, string id, string name, bool isMissing, bool isSelected)
    {
        _owner = owner;
        Id = id;
        Name = isMissing ? L("{0} (missing)", id) : name;
        IsMissing = isMissing;
        IsSelected = isSelected;
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsMissing { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    partial void OnIsSelectedChanged(bool value) => _owner?.Toggled(this);
}

/// <summary>
/// <c>open</c>: a checklist of profiles, in the order of the profile list, which is also the order the picker shows them
/// in. The checked ones are stored in that order. Ids that no longer exist stay listed, marked missing, until unchecked.
/// </summary>
public sealed class OpenActionViewModel : ActionViewModel
{
    private readonly bool _ready;

    public OpenActionViewModel(ActionListViewModel list, OpenAction model)
        : base(list, model)
    {
        var stored = model.Profiles ?? [];
        var config = list.Settings.Config;
        var toggles = config.Profiles.Select(p => new ProfileToggleViewModel(this, p.Id, p.Name, false, stored.Contains(p.Id))).ToList();
        toggles.AddRange(stored.Where(id => config.Profiles.All(p => p.Id != id)).Distinct().Select(id => new ProfileToggleViewModel(this, id, id, true, true)));
        Profiles = new ObservableCollection<ProfileToggleViewModel>(toggles);
        _ready = true;
    }

    public override string Title => L("Open with");

    public ObservableCollection<ProfileToggleViewModel> Profiles { get; }

    /// <summary>No profile checked: the default profile opens (docs/Configuration.md §2.8).</summary>
    public bool UsesDefaultProfile => !Profiles.Any(p => p.IsSelected);

    public override RoutingAction ToModel()
    {
        var ids = Profiles.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        return new OpenAction { Profiles = ids.Count == 0 ? null : ids, ExtensionData = ExtensionData };
    }

    internal void Toggled(ProfileToggleViewModel toggle)
    {
        if (!_ready)
        {
            return;
        }
        OnPropertyChanged(nameof(UsesDefaultProfile));
        Changed();
    }
}

public sealed partial class RewriteActionViewModel : ActionViewModel
{
    private readonly bool _ready;

    public RewriteActionViewModel(ActionListViewModel list, RewriteAction model)
        : base(list, model)
    {
        Mode = model.Mode;
        Pattern = model.Pattern ?? "";
        Replacement = model.Replacement ?? "";
        Parameter = model.Parameter ?? "";
        _ready = true;
    }

    public override string Title => L("Rewrite the URL");

    public static IReadOnlyList<EnumChoice<RewriteMode>> Modes { get; } =
    [
        new(RewriteMode.Regex, L("Replace with a regular expression")),
        new(RewriteMode.QueryParameter, L("Use a query parameter as the URL")),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRegex), nameof(IsQueryParameter), nameof(SelectedMode))]
    public partial RewriteMode Mode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PatternError))]
    public partial string Pattern { get; set; } = "";

    [ObservableProperty]
    public partial string Replacement { get; set; } = "";

    [ObservableProperty]
    public partial string Parameter { get; set; } = "";

    public EnumChoice<RewriteMode> SelectedMode
    {
        get => Modes.First(m => m.Value == Mode);
        set => Mode = value?.Value ?? Mode;
    }

    public bool IsRegex => Mode == RewriteMode.Regex;

    public bool IsQueryParameter => Mode == RewriteMode.QueryParameter;

    public string? PatternError => Pattern.Length == 0 ? null : PatternMatcher.Create(MatchKind.Regex, Pattern, ignoreCase: false).Error;

    partial void OnModeChanged(RewriteMode value) => Write();

    partial void OnPatternChanged(string value) => Write();

    partial void OnReplacementChanged(string value) => Write();

    partial void OnParameterChanged(string value) => Write();

    // Switching the mode keeps ExtensionData: the action is still a rewrite.
    public override RoutingAction ToModel() => IsRegex
        ? new RewriteAction { Mode = RewriteMode.Regex, Pattern = Pattern, Replacement = Replacement, ExtensionData = ExtensionData }
        : new RewriteAction { Mode = RewriteMode.QueryParameter, Parameter = Parameter, ExtensionData = ExtensionData };

    private void Write()
    {
        if (_ready)
        {
            Changed();
        }
    }
}

/// <summary><c>system</c>, <c>none</c> and <c>reroute</c>: nothing to configure.</summary>
public sealed class SimpleActionViewModel : ActionViewModel
{
    private readonly RoutingAction _model;

    public SimpleActionViewModel(ActionListViewModel list, RoutingAction model, string title, string description)
        : base(list, model)
    {
        _model = model;
        Title = title;
        Description = description;
    }

    public override string Title { get; }

    public string Description { get; }

    // Nothing here changes the action, so it is stored again as it is, with whatever a newer Preferis added to it.
    public override RoutingAction ToModel() => _model;
}

/// <summary>An action written by a newer Preferis. Shown read-only and saved back unchanged.</summary>
public sealed class UnknownActionViewModel : ActionViewModel
{
    private readonly UnknownAction _model;

    public UnknownActionViewModel(ActionListViewModel list, UnknownAction model)
        : base(list, model)
    {
        _model = model;
    }

    public override string Title => L("Unknown action “{0}”", Type);

    public string Type => _model.Type;

    public string Json => JsonSerializer.Serialize(_model.Raw, PreferisJsonContext.Default.JsonElement);

    public override RoutingAction ToModel() => _model;
}
