using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;
using Preferis.Settings.Services;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

public enum ProfileTargetKind
{
    Browser,
    Command,

    /// <summary>No shared target: each computer sets its own (docs/Design.md §7.4). Not a kind of this computer's target.</summary>
    EachComputer,
}

/// <param name="Title">What the list shows, e.g. "Brave (not installed)".</param>
public sealed record BrowserChoice(string Id, string Title, bool IsInstalled, byte[]? IconPng) : Choice(Title);

/// <param name="Reference">What is stored in the profile; null for "(None)".</param>
public sealed record BrowserProfileChoice(BrowserProfileRef? Reference, string Title) : Choice(Title);

/// <param name="Id">Profile id; null for "(None)".</param>
public sealed record ProfileChoice(string? Id, string Title) : Choice(Title);

/// <summary>
/// Editor of one profile. Every edit is written to <see cref="Model"/> right away. Values of the target kind that
/// is not in use (browser vs. command) are kept here, so switching back and forth loses nothing.
/// </summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private static readonly BrowserProfileChoice NoBrowserProfile = new(null, L("(None)"));
    private static readonly ProfileChoice NoFallback = new(null, L("(None)"));

    private readonly SettingsViewModel _settings;
    private bool _syncing;

    public ProfileViewModel(SettingsViewModel settings, Profile model)
    {
        _settings = settings;
        Model = model;
        BrowserChoices = BuildBrowserChoices(settings.Browsers, Platform, model.Browser?.Default, model.Browser?.For(Platform));
        // This machine's OS chooses from what is installed here; the other OS from the catalog.
        MacOSBrowserChoices = Platform == HostPlatform.MacOS ? BrowserChoices : BuildCatalogChoices(model, HostPlatform.MacOS);
        WindowsBrowserChoices = Platform == HostPlatform.Windows ? BrowserChoices : BuildCatalogChoices(model, HostPlatform.Windows);
        Reload();
    }

    public Profile Model { get; }

    public string Id => Model.Id;

    /// <summary>The id being typed; <see cref="ChangeIdCommand"/> applies it (docs/Configuration.md §2.11).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeIdCommand))]
    public partial string IdDraft { get; set; } = "";

    /// <summary>Why <see cref="IdDraft"/> cannot be applied, or null.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChangeIdCommand))]
    public partial string? IdError { get; private set; }

    private HostPlatform Platform => _settings.Platform.Platform;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>What the shared target is; <see cref="OpensCommand"/> says what this machine opens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowser), nameof(IsCommand), nameof(IsEachComputer), nameof(OpensCommand))]
    public partial ProfileTargetKind TargetKind { get; set; }

    public bool IsBrowser => TargetKind == ProfileTargetKind.Browser;

    public bool IsCommand => TargetKind == ProfileTargetKind.Command;

    /// <summary>
    /// No shared target: <see cref="LocalTarget"/> is what this computer opens, and is there to choose from even before
    /// this computer has a target in <c>local.json</c>; the first choice adds it.
    /// </summary>
    public bool IsEachComputer => TargetKind == ProfileTargetKind.EachComputer;

    /// <summary>This machine runs a command for the profile (its own target or the shared one), for the icon.</summary>
    public bool OpensCommand => LocalTarget is { } local ? local.IsCommand : IsCommand;

    public IReadOnlyList<ProfileTargetKind> TargetKinds { get; } = [ProfileTargetKind.Browser, ProfileTargetKind.Command, ProfileTargetKind.EachComputer];

    /// <summary>Browsers for this machine's OS: detected ones first, then the rest of the catalog.</summary>
    public IReadOnlyList<BrowserChoice> BrowserChoices { get; }

    /// <summary>Browsers for macOS with <see cref="PerOs"/>: <see cref="BrowserChoices"/> on a Mac, the catalog elsewhere.</summary>
    public IReadOnlyList<BrowserChoice> MacOSBrowserChoices { get; }

    /// <summary>Browsers for Windows with <see cref="PerOs"/>: <see cref="BrowserChoices"/> on Windows, the catalog elsewhere.</summary>
    public IReadOnlyList<BrowserChoice> WindowsBrowserChoices { get; }

    /// <summary>The browser on every OS (when <see cref="PerOs"/> is off).</summary>
    [ObservableProperty]
    public partial BrowserChoice? Browser { get; set; }

    [ObservableProperty]
    public partial bool PerOs { get; set; }

    [ObservableProperty]
    public partial BrowserChoice? MacOSBrowser { get; set; }

    [ObservableProperty]
    public partial BrowserChoice? WindowsBrowser { get; set; }

    public ObservableCollection<BrowserProfileChoice> BrowserProfileChoices { get; } = [];

    [ObservableProperty]
    public partial BrowserProfileChoice? BrowserProfile { get; set; }

    public bool HasBrowserProfiles => BrowserProfileChoices.Count > 1;

    [ObservableProperty]
    public partial bool Private { get; set; }

    /// <summary>One argument per line.</summary>
    [ObservableProperty]
    public partial string ExtraArgs { get; set; } = "";

    [ObservableProperty]
    public partial string MacOSCommandPath { get; set; } = "";

    [ObservableProperty]
    public partial string MacOSCommandArgs { get; set; } = "";

    [ObservableProperty]
    public partial string WindowsCommandPath { get; set; } = "";

    [ObservableProperty]
    public partial string WindowsCommandArgs { get; set; } = "";

    public ObservableCollection<ProfileChoice> FallbackChoices { get; } = [];

    [ObservableProperty]
    public partial ProfileChoice? Fallback { get; set; }

    [ObservableProperty]
    public partial bool IsDefault { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIssues), nameof(HasErrors))]
    public partial IReadOnlyList<ConfigIssue> Issues { get; set; } = [];

    public bool HasIssues => Issues.Count > 0;

    /// <summary>Some of <see cref="Issues"/> are errors; the rest are warnings, such as a browser this machine does not have.</summary>
    public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

    /// <summary>
    /// This machine opens <see cref="LocalTarget"/> instead of the shared target (<c>local.json</c> <c>profileTargets</c>,
    /// docs/Configuration.md §3.1). Turning it on starts from what the shared target opens here.
    /// </summary>
    [ObservableProperty]
    public partial bool UsesLocalTarget { get; set; }

    /// <summary>The editor of this machine's target while <see cref="UsesLocalTarget"/> is on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpensCommand))]
    public partial LocalTargetViewModel? LocalTarget { get; private set; }

    /// <summary>Second line in the profile list: what this machine opens.</summary>
    public string Summary
    {
        get
        {
            if (LocalTarget is { } local)
            {
                return IsEachComputer && !local.IsSet ? L("Not set on this computer") : L("This computer: {0}", local.Summary);
            }
            if (IsCommand)
            {
                return CommandSummary(Platform == HostPlatform.MacOS ? MacOSCommandPath : WindowsCommandPath);
            }
            var browser = PerOs
                ? L("{0} on macOS, {1} on Windows", MacOSBrowser?.Title ?? "?", WindowsBrowser?.Title ?? "?")
                : Browser?.Title ?? L("No browser");
            return BrowserSummary(browser, BrowserProfile?.Reference, Private);
        }
    }

    /// <summary>Icon of the browser used on this machine.</summary>
    public byte[]? IconPng => _settings.Browsers.FirstOrDefault(b => b.Browser.Id == (LocalTarget is { } local ? local.BrowserId : CurrentBrowserId))?.IconPng;

    private string? CurrentBrowserId => PerOs ? (Platform == HostPlatform.MacOS ? MacOSBrowser : WindowsBrowser)?.Id : Browser?.Id;

    /// <summary>Reads everything from <see cref="Model"/> again (after it was changed from outside this editor).</summary>
    public void Reload()
    {
        _syncing = true;
        try
        {
            var model = Model;
            Name = model.Name;
            TargetKind = model.Command is not null ? ProfileTargetKind.Command
                : model.Browser is not null ? ProfileTargetKind.Browser
                : ProfileTargetKind.EachComputer;

            var reference = model.Browser;
            PerOs = reference is not null && !reference.IsPlain;
            Browser = Find(BrowserChoices, reference?.Default ?? reference?.For(Platform));
            MacOSBrowser = Find(MacOSBrowserChoices, reference?.For(HostPlatform.MacOS));
            WindowsBrowser = Find(WindowsBrowserChoices, reference?.For(HostPlatform.Windows));
            Private = model.Private;
            ExtraArgs = string.Join("\n", model.ExtraArgs ?? []);

            MacOSCommandPath = model.Command?.MacOS?.Path ?? "";
            MacOSCommandArgs = string.Join("\n", model.Command?.MacOS?.Args ?? []);
            WindowsCommandPath = model.Command?.Windows?.Path ?? "";
            WindowsCommandArgs = string.Join("\n", model.Command?.Windows?.Args ?? []);

            BuildBrowserProfileChoices();

            var local = LocalProfileTargets.Find(_settings.Local, model, _settings.Config) ?? (IsEachComputer ? new ProfileTarget() : null);
            UsesLocalTarget = local is not null;
            LocalTarget = local is null ? null : new LocalTargetViewModel(this, _settings, local);

            IdDraft = Id;
            RefreshRelations();
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IconPng));
    }

    /// <summary>Default mark and fallback list, which depend on the other profiles.</summary>
    internal void RefreshRelations()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            IsDefault = _settings.Config.DefaultProfile == Id;
            FallbackChoices.Clear();
            FallbackChoices.Add(NoFallback);
            foreach (var other in _settings.Config.Profiles.Where(p => p.Id != Id))
            {
                FallbackChoices.Add(new ProfileChoice(other.Id, other.Name));
            }
            if (Model.Fallback is { } fallback && FallbackChoices.All(c => c.Id != fallback))
            {
                FallbackChoices.Add(new ProfileChoice(fallback, L("{0} (missing)", fallback)));
            }
            Fallback = FallbackChoices.First(c => c.Id == Model.Fallback);
            // Another profile may have taken (or given up) the id being typed.
            IdError = ProfileIds.Validate(_settings.Config, Id, IdDraft.Trim());
        }
        finally
        {
            _syncing = wasSyncing;
        }
    }

    partial void OnIdDraftChanged(string value) => IdError = ProfileIds.Validate(_settings.Config, Id, value.Trim());

    private bool CanChangeId => IdError is null && IdDraft.Trim() != Id;

    /// <summary>Gives the profile the id in <see cref="IdDraft"/> and rewrites everything that refers to it.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeId))]
    private void ChangeId()
    {
        ProfileIds.Rename(_settings.Config, _settings.Local, Id, IdDraft.Trim());
        OnPropertyChanged(nameof(Id));
        IdDraft = Id;
        // Fallback lists, the default mark and the issues are keyed by id.
        _settings.Profiles.ProfilesChanged();
    }

    partial void OnUsesLocalTargetChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }
        if (value)
        {
            var target = LocalProfileTargets.FromShared(Model, Platform);
            (_settings.Local.ProfileTargets ??= [])[Id] = target;
            LocalTarget = new LocalTargetViewModel(this, _settings, target);
        }
        else
        {
            ForgetLocalTarget(_settings, Model);
            LocalTarget = null;
        }
        OnLocalTargetChanged();
    }

    /// <summary>Called by <see cref="LocalTarget"/> after every edit.</summary>
    internal void OnLocalTargetChanged()
    {
        if (LocalTarget is { } target && !HasStoredLocalTarget)
        {
            // Set on each computer, and chosen on this one for the first time.
            (_settings.Local.ProfileTargets ??= [])[Id] = target.Model;
        }
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IconPng));
        OnPropertyChanged(nameof(OpensCommand));
        _settings.MarkChanged();
    }

    private bool HasStoredLocalTarget => LocalProfileTargets.Find(_settings.Local, Model, _settings.Config) is not null;

    /// <summary>Removes the local target of <paramref name="profile"/>, under its id or one of its previous ids.</summary>
    internal static void ForgetLocalTarget(SettingsViewModel settings, Profile profile)
    {
        if (settings.Local.ProfileTargets is not { } targets)
        {
            return;
        }
        targets.Remove(profile.Id);
        // A previous id that is now another profile's id is that profile's key.
        foreach (var previous in (profile.PreviousIds ?? []).Where(id => settings.Config.Profiles.All(p => p.Id != id)))
        {
            targets.Remove(previous);
        }
        if (targets.Count == 0)
        {
            settings.Local.ProfileTargets = null;
        }
    }

    partial void OnNameChanged(string value)
    {
        if (_syncing)
        {
            return;
        }
        Model.Name = value;
        // Other profiles show this name in their fallback lists.
        _settings.Profiles?.ProfilesChanged();
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnTargetKindChanged(ProfileTargetKind value)
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        try
        {
            if (value == ProfileTargetKind.EachComputer && LocalTarget is null)
            {
                // This computer goes on opening what it opened; the others set their own.
                var target = LocalProfileTargets.FromShared(Model, Platform);
                (_settings.Local.ProfileTargets ??= [])[Id] = target;
                UsesLocalTarget = true;
                LocalTarget = new LocalTargetViewModel(this, _settings, target);
            }
            else if (value != ProfileTargetKind.EachComputer && LocalTarget is not null && !HasStoredLocalTarget)
            {
                // Nothing was chosen for this computer: the shared target is all there is.
                UsesLocalTarget = false;
                LocalTarget = null;
            }
            if (value == ProfileTargetKind.Browser && !PerOs && Browser is null)
            {
                // A shared target without a browser would be read back as set on each computer.
                Browser = BrowserChoices.FirstOrDefault();
                BuildBrowserProfileChoices();
            }
        }
        finally
        {
            _syncing = false;
        }
        WriteTarget();
    }

    partial void OnBrowserChanged(BrowserChoice? value)
    {
        if (_syncing)
        {
            return;
        }
        // A browser profile of one browser means nothing to another.
        BrowserProfile = null;
        BuildBrowserProfileChoices();
        WriteTarget();
    }

    partial void OnPerOsChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        try
        {
            // Both OS start from this machine's browser; turning it off keeps this machine's.
            if (value)
            {
                MacOSBrowser = Find(MacOSBrowserChoices, Browser?.Id);
                WindowsBrowser = Find(WindowsBrowserChoices, Browser?.Id);
            }
            else
            {
                Browser = Find(BrowserChoices, (Platform == HostPlatform.MacOS ? MacOSBrowser : WindowsBrowser)?.Id);
            }
        }
        finally
        {
            _syncing = false;
        }
        WriteTarget();
    }

    partial void OnMacOSBrowserChanged(BrowserChoice? value) => OnPerOsBrowserChanged(HostPlatform.MacOS);

    partial void OnWindowsBrowserChanged(BrowserChoice? value) => OnPerOsBrowserChanged(HostPlatform.Windows);

    private void OnPerOsBrowserChanged(HostPlatform os)
    {
        if (_syncing)
        {
            return;
        }
        if (Platform == os)
        {
            // This machine's browser changed: its browser profiles are the ones to choose from.
            BrowserProfile = null;
            BuildBrowserProfileChoices();
        }
        WriteTarget();
    }

    partial void OnBrowserProfileChanged(BrowserProfileChoice? value) => WriteTarget();

    partial void OnPrivateChanged(bool value) => WriteTarget();

    partial void OnExtraArgsChanged(string value) => WriteTarget();

    partial void OnMacOSCommandPathChanged(string value) => WriteTarget();

    partial void OnMacOSCommandArgsChanged(string value) => WriteTarget();

    partial void OnWindowsCommandPathChanged(string value) => WriteTarget();

    partial void OnWindowsCommandArgsChanged(string value) => WriteTarget();

    partial void OnFallbackChanged(ProfileChoice? value)
    {
        if (_syncing)
        {
            return;
        }
        Model.Fallback = value?.Id;
        _settings.MarkChanged();
    }

    [RelayCommand]
    private async Task BrowseMacOSCommand()
    {
        if (await _settings.Dialogs.PickFileAsync(L("Choose an application or executable")) is not { } path)
        {
            return;
        }
        _syncing = true;
        try
        {
            var (commandPath, args) = MacOSCommandFor(path);
            MacOSCommandPath = commandPath;
            MacOSCommandArgs = args ?? MacOSCommandArgs;
        }
        finally
        {
            _syncing = false;
        }
        WriteTarget();
    }

    /// <summary>The command for a file chosen on macOS; <c>Args</c> is null when the arguments stay as they are.</summary>
    internal static (string Path, string? Args) MacOSCommandFor(string chosen)
        // An app bundle is a folder, not an executable: let Launch Services open the URL with it.
        => chosen.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? ("/usr/bin/open", $"-a\n{chosen.TrimEnd('/')}\n{{url}}")
            : (chosen, null);

    [RelayCommand]
    private async Task BrowseWindowsCommand()
    {
        if (await _settings.Dialogs.PickFileAsync(L("Choose a program")) is { } path)
        {
            WindowsCommandPath = path;
        }
    }

    private void WriteTarget()
    {
        if (_syncing)
        {
            return;
        }

        var model = Model;
        if (IsCommand)
        {
            model.Browser = null;
            model.BrowserProfile = null;
            model.Private = false;
            model.ExtraArgs = null;
            model.Command = new CommandTarget
            {
                MacOS = Spec(MacOSCommandPath, MacOSCommandArgs, model.Command?.MacOS),
                Windows = Spec(WindowsCommandPath, WindowsCommandArgs, model.Command?.Windows),
                ExtensionData = model.Command?.ExtensionData,
            };
        }
        else if (IsEachComputer)
        {
            // This computer's target is in local.json (LocalTarget), and every other computer's in its own.
            model.Command = null;
            model.Browser = null;
            model.BrowserProfile = null;
            model.Private = false;
            model.ExtraArgs = null;
        }
        else
        {
            model.Command = null;
            model.Browser = PerOs
                ? new BrowserReference { MacOS = MacOSBrowser?.Id, Windows = WindowsBrowser?.Id }
                : Browser is null ? null : new BrowserReference(Browser.Id);
            model.BrowserProfile = BrowserProfile?.Reference;
            model.Private = Private;
            model.ExtraArgs = Lines(ExtraArgs) is { Count: > 0 } args ? args : null;
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IconPng));
        _settings.MarkChanged();
    }

    // Rebuilt on every edit; properties a newer Preferis added to the spec are carried over.
    private static CommandSpec? Spec(string path, string args, CommandSpec? previous)
        => path.Length == 0 && args.Trim().Length == 0 ? null : new CommandSpec { Path = path.Trim(), Args = Lines(args), ExtensionData = previous?.ExtensionData };

    internal static List<string> Lines(string text)
        => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    internal static string CommandSummary(string path) => path.Length == 0 ? L("Command") : L("Command: {0}", Path.GetFileName(path));

    internal static string BrowserSummary(string browser, BrowserProfileRef? reference, bool isPrivate)
    {
        var details = reference is not null ? PickerDisplay.DetailLine(new PickerCandidate { BrowserProfile = reference, Private = isPrivate }) : isPrivate ? L("Private") : "";
        return details.Length == 0 ? browser : $"{browser} · {details}";
    }

    private void BuildBrowserProfileChoices()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            BrowserProfile = FillBrowserProfileChoices(BrowserProfileChoices, _settings.Browsers, CurrentBrowserId, Model.BrowserProfile);
        }
        finally
        {
            _syncing = wasSyncing;
        }
        OnPropertyChanged(nameof(HasBrowserProfiles));
    }

    /// <summary>
    /// Fills <paramref name="choices"/> with "(None)" and the browser profiles of <paramref name="browserId"/> on this
    /// machine, and returns the one to select for <paramref name="stored"/>.
    /// </summary>
    internal static BrowserProfileChoice FillBrowserProfileChoices(ObservableCollection<BrowserProfileChoice> choices, IReadOnlyList<DetectedBrowserInfo> browsers, string? browserId, BrowserProfileRef? stored)
    {
        choices.Clear();
        choices.Add(NoBrowserProfile);
        var selected = NoBrowserProfile;

        var detected = browsers.FirstOrDefault(b => b.Browser.Id == browserId);
        var family = detected?.Browser.Family ?? (browserId is null ? null : BrowserCatalog.Find(browserId)?.Family) ?? BrowserFamily.Other;
        var profiles = detected?.Profiles ?? [];
        var match = stored is null ? null : BrowserProfileMatcher.Match(profiles, stored, family);

        if (family != BrowserFamily.Other)
        {
            foreach (var profile in profiles)
            {
                var reference = ProfilesPageViewModel.ReferenceTo(profile, family);
                var title = PickerDisplay.DetailLine(new PickerCandidate { BrowserProfile = reference });
                if (ReferenceEquals(profile, match))
                {
                    // Keep the stored reference as it is (identifiers from another machine, unknown properties):
                    // an unrelated edit of this profile must not replace it with this machine's view.
                    selected = new BrowserProfileChoice(stored, title);
                    choices.Add(selected);
                }
                else
                {
                    choices.Add(new BrowserProfileChoice(reference, title));
                }
            }
        }

        if (stored is not null && match is null)
        {
            // Keep what is stored even when this machine does not have it: it may exist on another one.
            selected = new BrowserProfileChoice(stored, L("{0} (not found on this computer)", stored.Name ?? stored.Email ?? stored.Directory));
            choices.Add(selected);
        }
        return selected;
    }

    /// <summary>Browsers for this machine's OS: detected ones first, then the rest of the catalog, then <paramref name="storedIds"/> not among them.</summary>
    internal static IReadOnlyList<BrowserChoice> BuildBrowserChoices(IReadOnlyList<DetectedBrowserInfo> browsers, HostPlatform platform, params string?[] storedIds)
    {
        var choices = browsers.Select(b => new BrowserChoice(b.Browser.Id, b.Browser.DisplayName, true, b.IconPng)).ToList();
        choices.AddRange(BrowserCatalog.Entries
            .Where(e => choices.All(c => c.Id != e.Id) && ExistsOn(e, platform))
            .Select(e => new BrowserChoice(e.Id, L("{0} (not installed)", e.DisplayName), false, null)));
        AddStoredIds(choices, storedIds);
        return choices;
    }

    /// <summary>The other OS of a profile shared between machines: nothing is installed here to choose from, the catalog is.</summary>
    private static IReadOnlyList<BrowserChoice> BuildCatalogChoices(Profile model, HostPlatform os)
    {
        var choices = BrowserCatalog.Entries
            .Where(e => ExistsOn(e, os))
            .Select(e => new BrowserChoice(e.Id, e.DisplayName, false, null))
            .ToList();
        AddStoredIds(choices, model.Browser?.For(os), model.Browser?.Default);
        return choices;
    }

    private static bool ExistsOn(BrowserCatalogEntry entry, HostPlatform os) => os == HostPlatform.MacOS
        ? entry.MacBundleId is not null
        : entry.WindowsStartMenuKey is not null || entry.WindowsStartMenuKeyPrefix is not null || entry.WindowsKnownPaths.Count > 0;

    // Ids the configuration uses that are neither detected nor in the catalog (mac:/win: ids from another machine).
    private static void AddStoredIds(List<BrowserChoice> choices, params string?[] ids)
    {
        foreach (var id in ids.OfType<string>().Distinct().Where(id => choices.All(c => c.Id != id)))
        {
            choices.Add(new BrowserChoice(id, L("{0} (not installed)", id), false, null));
        }
    }

    internal static BrowserChoice? Find(IReadOnlyList<BrowserChoice> choices, string? id) => id is null ? null : choices.FirstOrDefault(c => c.Id == id);
}
