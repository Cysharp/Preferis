using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Profiles;
using Preferis.Settings.Services;

namespace Preferis.Settings.ViewModels;

/// <summary>The profiles page: the configured profiles and the browsers detected on this machine to create them from.</summary>
public sealed partial class ProfilesPageViewModel : PageViewModel
{
    public ProfilesPageViewModel(SettingsViewModel settings)
        : base(settings)
    {
        Profiles = new ObservableCollection<ProfileViewModel>(settings.Config.Profiles.Select(p => new ProfileViewModel(settings, p)));
        DetectedBrowsers = new ObservableCollection<DetectedBrowserViewModel>(settings.Browsers.Select(b => new DetectedBrowserViewModel(b)));
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public override string Title => "Profiles";

    public ObservableCollection<ProfileViewModel> Profiles { get; }

    public ObservableCollection<DetectedBrowserViewModel> DetectedBrowsers { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand), nameof(DeleteCommand), nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(SetDefaultCommand))]
    public partial ProfileViewModel? SelectedProfile { get; set; }

    private bool HasSelection => SelectedProfile is not null;

    private bool CanMoveUp => SelectedProfile is not null && Profiles.IndexOf(SelectedProfile) > 0;

    private bool CanMoveDown => SelectedProfile is not null && Profiles.IndexOf(SelectedProfile) < Profiles.Count - 1;

    [RelayCommand]
    private void AddDetected(DetectedItemViewModel item)
        => AddProfile(CreateFromDetected(item.Browser.Info.Browser, item.Profile, Settings.Config.Profiles.Select(p => p.Id)));

    /// <summary>One profile for every detected browser profile (or browser without profiles) that no profile uses yet.</summary>
    [RelayCommand]
    private void AddAllDetected()
    {
        foreach (var info in Settings.Browsers)
        {
            var browser = info.Browser;
            var targets = info.Profiles is { Count: > 0 } profiles ? profiles.Select(p => (BrowserProfileInfo?)p) : [null];
            foreach (var target in targets.Where(t => !IsConfigured(browser, t)))
            {
                AddProfile(CreateFromDetected(browser, target, Settings.Config.Profiles.Select(p => p.Id)));
            }
        }
    }

    [RelayCommand]
    private void AddCommandProfile()
    {
        var name = "Command";
        AddProfile(new Profile
        {
            Id = IdGenerator.ForName(name, "command", Settings.Config.Profiles.Select(p => p.Id)),
            Name = name,
            Command = new CommandTarget { MacOS = new CommandSpec { Path = "", Args = ["{url}"] } },
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var source = SelectedProfile!;
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(source.Model, PreferisJsonContext.Default.Profile), PreferisJsonContext.Default.Profile)!;
        copy.Id = IdGenerator.Unique(source.Id, Settings.Config.Profiles.Select(p => p.Id));
        copy.Name = source.Name + " copy";
        AddProfile(copy, Profiles.IndexOf(source) + 1);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Delete()
    {
        var profile = SelectedProfile!;
        var usages = Usages(Settings.Config, profile.Id);
        if (usages.Count > 0)
        {
            var message = "It is used by:\n• " + string.Join("\n• ", usages) + "\n\nThese references will be removed.";
            if (await Settings.Dialogs.AskAsync($"Delete “{profile.Name}”?", message, ["Delete", "Cancel"]) != 0)
            {
                return;
            }
        }

        RemoveReferences(Settings.Config, profile.Id);
        var index = Profiles.IndexOf(profile);
        Settings.Config.Profiles.Remove(profile.Model);
        Profiles.Remove(profile);
        SelectedProfile = Profiles.Count == 0 ? null : Profiles[Math.Min(index, Profiles.Count - 1)];
        ProfilesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(+1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetDefault()
    {
        Settings.Config.DefaultProfile = SelectedProfile!.Id;
        ProfilesChanged();
    }

    [RelayCommand]
    private void RefreshBrowsers() => Settings.RedetectBrowsers();

    internal void UpdateIssues(IReadOnlyList<ConfigIssue> issues)
    {
        foreach (var profile in Profiles)
        {
            profile.Issues = issues.Where(i => i.Scope == IssueScope.Profile && i.Id == profile.Id).ToList();
        }
    }

    /// <summary>The set, order, names or default changed: every editor's fallback list and default mark follow.</summary>
    internal void ProfilesChanged()
    {
        foreach (var profile in Profiles)
        {
            profile.RefreshRelations();
        }
        Settings.MarkChanged();
    }

    internal static Profile CreateFromDetected(DetectedBrowser browser, BrowserProfileInfo? info, IEnumerable<string> existingIds)
    {
        var label = info is null ? null : info.Name ?? info.Email ?? info.Directory;
        var name = label is null ? browser.DisplayName : $"{browser.DisplayName} ({label})";
        return new Profile
        {
            Id = IdGenerator.ForName(name, "profile", existingIds),
            Name = name,
            Browser = new BrowserReference(browser.Id),
            BrowserProfile = info is null ? null : ReferenceTo(info, browser.Family),
        };
    }

    /// <summary>Everything that identifies a browser profile, so it can be found on another machine (docs/Design.md §7.2).</summary>
    internal static BrowserProfileRef ReferenceTo(BrowserProfileInfo info, BrowserFamily family) => family == BrowserFamily.Firefox
        // Firefox profiles are selected by name; the directory is a machine specific path.
        ? new BrowserProfileRef { Name = info.Name ?? info.Directory }
        : new BrowserProfileRef { Directory = info.Directory, Name = info.Name, Email = info.Email };

    private bool IsConfigured(DetectedBrowser browser, BrowserProfileInfo? target)
        => Settings.Config.Profiles.Any(p =>
            p.Command is null
            && p.Browser?.For(Settings.Platform.Platform) == browser.Id
            && (target is null
                ? p.BrowserProfile is null
                : p.BrowserProfile is { } reference && BrowserProfileMatcher.Match([target], reference, browser.Family) is not null));

    private void AddProfile(Profile profile, int? index = null)
    {
        var config = Settings.Config;
        var at = index ?? config.Profiles.Count;
        config.Profiles.Insert(at, profile);
        // The very first profile is what "open" without a profile should use until the user says otherwise.
        config.DefaultProfile ??= profile.Id;
        var viewModel = new ProfileViewModel(Settings, profile);
        Profiles.Insert(at, viewModel);
        SelectedProfile = viewModel;
        ProfilesChanged();
    }

    private void Move(int delta)
    {
        var profile = SelectedProfile!;
        var from = Profiles.IndexOf(profile);
        var to = from + delta;
        Profiles.Move(from, to);
        Settings.Config.Profiles.RemoveAt(from);
        Settings.Config.Profiles.Insert(to, profile.Model);
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        ProfilesChanged();
    }

    private static List<string> Usages(PreferisConfig config, string id)
    {
        var usages = new List<string>();
        if (config.DefaultProfile == id)
        {
            usages.Add("the default profile setting");
        }
        usages.AddRange(config.Profiles.Where(p => p.Fallback == id).Select(p => $"the fallback of “{p.Name}”"));
        usages.AddRange(config.Rules.Where(r => Opens(r.Actions, id)).Select(r => $"rule “{r.Name}” ({r.Id})"));
        if (Opens(config.CatchAll.Actions, id))
        {
            usages.Add("the catch-all rule");
        }
        return usages;
    }

    private static bool Opens(List<RoutingAction> actions, string id) => actions.OfType<OpenAction>().Any(a => a.Profiles?.Contains(id) == true);

    private static void RemoveReferences(PreferisConfig config, string id)
    {
        if (config.DefaultProfile == id)
        {
            config.DefaultProfile = null;
        }
        foreach (var profile in config.Profiles.Where(p => p.Fallback == id))
        {
            profile.Fallback = null;
        }
        foreach (var open in config.Rules.SelectMany(r => r.Actions).Concat(config.CatchAll.Actions).OfType<OpenAction>())
        {
            if (open.Profiles?.Remove(id) == true && open.Profiles.Count == 0)
            {
                open.Profiles = null;
            }
        }
    }
}

/// <summary>A browser detected on this machine, with the entries a profile can be created from.</summary>
public sealed class DetectedBrowserViewModel
{
    public DetectedBrowserViewModel(DetectedBrowserInfo info)
    {
        Info = info;
        var items = new List<DetectedItemViewModel> { new(this, null, info.Browser.DisplayName, info.Profiles is { Count: > 0 } ? "Without a specific browser profile" : null) };
        items.AddRange((info.Profiles ?? []).Select(p => new DetectedItemViewModel(
            this,
            p,
            p.Name ?? p.Email ?? p.Directory,
            p.Email is not null && p.Email != p.Name ? p.Email : null)));
        Items = items;
    }

    public DetectedBrowserInfo Info { get; }

    public string Id => Info.Browser.Id;

    public string Name => Info.Browser.DisplayName;

    public byte[]? IconPng => Info.IconPng;

    /// <summary>Why no browser profiles are listed, when that is not obvious.</summary>
    public string? Note => Info.Profiles is null && Info.Browser.Family != BrowserFamily.Other
        ? "Its browser profiles could not be read. On macOS 27 and later, give Preferis Settings Full Disk Access to list them."
        : null;

    public IReadOnlyList<DetectedItemViewModel> Items { get; }
}

/// <summary>The browser itself (<see cref="Profile"/> null) or one of its browser profiles.</summary>
public sealed record DetectedItemViewModel(DetectedBrowserViewModel Browser, BrowserProfileInfo? Profile, string Title, string? Subtitle)
{
    public byte[]? AvatarPng => Profile?.AvatarPath is { } path && File.Exists(path) ? File.ReadAllBytes(path) : null;
}
