using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Profiles;
using Preferis.Settings.Services;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>The profiles page: the configured profiles and the browsers detected on this machine to create them from.</summary>
public sealed partial class ProfilesPageViewModel : PageViewModel
{
    public ProfilesPageViewModel(SettingsViewModel settings)
        : base(settings)
    {
        Profiles = new ObservableCollection<ProfileViewModel>(settings.Config.Profiles.Select(p => new ProfileViewModel(settings, p)));
        DetectedBrowsers = new ObservableCollection<DetectedBrowserViewModel>(settings.Browsers.Select(b => new DetectedBrowserViewModel(b, settings.Platform.Platform)));
        SelectedProfile = Profiles.FirstOrDefault();
    }

    public override string Title => L("Profiles");

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
        => AddProfile(CreateFromDetected(item.Browser.Info.Browser, item.Profile));

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
                AddProfile(CreateFromDetected(browser, target));
            }
        }
    }

    [RelayCommand]
    private void AddCommandProfile()
    {
        var name = UniqueName(L("Command"));
        var spec = new CommandSpec { Path = "", Args = ["{url}"] };
        AddProfile(new Profile
        {
            Id = NewId(),
            Name = name,
            Command = Settings.Platform.Platform == HostPlatform.Windows ? new CommandTarget { Windows = spec } : new CommandTarget { MacOS = spec },
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var source = SelectedProfile!;
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(source.Model, PreferisJsonContext.Default.Profile), PreferisJsonContext.Default.Profile)!;
        copy.Id = NewId();
        // The ids it had belong to the original.
        copy.PreviousIds = null;
        copy.Name = UniqueName(L("{0} copy", source.Name));
        if (LocalProfileTargets.Find(Settings.Local, source.Model, Settings.Config) is { } target)
        {
            Settings.Local.ProfileTargets![copy.Id] = LocalProfileTargets.Copy(target);
        }
        AddProfile(copy, Profiles.IndexOf(source) + 1);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Delete()
    {
        var profile = SelectedProfile!;
        var usages = Usages(Settings.Config, profile.Id);
        if (usages.Count > 0)
        {
            var message = L("It is used by:\n{0}\n\nThese references will be removed.", "• " + string.Join("\n• ", usages));
            if (await Settings.Dialogs.AskAsync(L("Delete “{0}”?", profile.Name), message, [L("Delete"), L("Cancel")]) != 0)
            {
                return;
            }
        }

        RemoveReferences(Settings.Config, profile.Id);
        ProfileViewModel.ForgetLocalTarget(Settings, profile.Model);
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

    private Profile CreateFromDetected(DetectedBrowser browser, BrowserProfileInfo? info)
    {
        var label = info is null ? null : info.Name ?? info.Email ?? info.Directory;
        return new Profile
        {
            Id = NewId(),
            Name = UniqueName(label is null ? browser.DisplayName : $"{browser.DisplayName} ({label})"),
            Browser = new BrowserReference(browser.Id),
            BrowserProfile = info is null ? null : ReferenceTo(info, browser.Family),
        };
    }

    /// <summary>
    /// <c>profile-N</c> (docs/Configuration.md §2.10), not from the name: the first name is the browser's, and the id would
    /// keep it after a rename. Numbers that this machine's targets or previous ids use are not given out again.
    /// </summary>
    private string NewId() => IdGenerator.NextProfileId(ProfileIds.Taken(Settings.Config).Concat(Settings.Local.ProfileTargets?.Keys ?? Enumerable.Empty<string>()));

    // The picker tells profiles apart by name.
    private string UniqueName(string name) => ProfileNames.Unique(name, Settings.Config.Profiles.Select(p => p.Name));

    /// <summary>Everything that identifies a browser profile, so it can be found on another machine (docs/Design.md §7.2).</summary>
    internal static BrowserProfileRef ReferenceTo(BrowserProfileInfo info, BrowserFamily family) => family == BrowserFamily.Firefox
        // Firefox profiles are selected by name; the directory is a machine specific path.
        ? new BrowserProfileRef { Name = info.Name ?? info.Directory }
        : new BrowserProfileRef { Directory = info.Directory, Name = info.Name, Email = info.Email };

    // What this machine opens counts, its own targets included.
    private bool IsConfigured(DetectedBrowser browser, BrowserProfileInfo? target)
        => LocalProfileTargets.Apply(Settings.Config, Settings.Local).Profiles.Any(p =>
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

    private void Move(int delta) => MoveProfile(SelectedProfile!, Profiles.IndexOf(SelectedProfile!) + delta);

    /// <summary>Moves a profile to <paramref name="index"/> (drag and drop in the list). The order is the picker's order.</summary>
    public void MoveProfile(ProfileViewModel profile, int index)
    {
        var from = Profiles.IndexOf(profile);
        var to = Math.Clamp(index, 0, Profiles.Count - 1);
        if (from < 0 || from == to)
        {
            return;
        }
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
            usages.Add(L("the default profile setting"));
        }
        usages.AddRange(config.Profiles.Where(p => p.Fallback == id).Select(p => L("the fallback of “{0}”", p.Name)));
        usages.AddRange(config.Rules.Where(r => Opens(r.Actions, id)).Select(r => L("rule “{0}” ({1})", r.Name, r.Id)));
        if (Opens(config.CatchAll.Actions, id))
        {
            usages.Add(L("the catch-all rule"));
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
    private readonly HostPlatform _platform;

    public DetectedBrowserViewModel(DetectedBrowserInfo info, HostPlatform platform = HostPlatform.MacOS)
    {
        Info = info;
        _platform = platform;
        var items = new List<DetectedItemViewModel> { new(this, null, info.Browser.DisplayName, info.Profiles is { Count: > 0 } ? L("Without a specific browser profile") : null) };
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
        ? _platform == HostPlatform.MacOS
            ? L("Its browser profiles could not be read. On macOS 27 and later, give Preferis Settings Full Disk Access to list them.")
            // On Windows nothing denies access; the profile list does not exist until the browser has run once.
            : L("Its browser profiles could not be read. Start the browser once so that it creates them.")
        : null;

    public IReadOnlyList<DetectedItemViewModel> Items { get; }
}

/// <summary>The browser itself (<see cref="Profile"/> null) or one of its browser profiles.</summary>
public sealed record DetectedItemViewModel(DetectedBrowserViewModel Browser, BrowserProfileInfo? Profile, string Title, string? Subtitle)
{
    public byte[]? AvatarPng => Profile?.AvatarPath is { } path && File.Exists(path) ? File.ReadAllBytes(path) : null;
}
