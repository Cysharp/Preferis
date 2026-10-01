using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core;
using Preferis.Core.Browsers;
using Preferis.Core.Configuration;
using Preferis.Core.Picker;
using Preferis.Core.Profiles;

namespace Preferis.Settings.ViewModels;

public enum ProfileTargetKind
{
    Browser,
    Command,
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
    private static readonly BrowserProfileChoice NoBrowserProfile = new(null, "(None)");
    private static readonly ProfileChoice NoFallback = new(null, "(None)");

    private readonly SettingsViewModel _settings;
    private bool _syncing;

    public ProfileViewModel(SettingsViewModel settings, Profile model)
    {
        _settings = settings;
        Model = model;
        BrowserChoices = BuildBrowserChoices(model);
        WindowsBrowserChoices = BuildWindowsBrowserChoices(model);
        Reload();
    }

    public Profile Model { get; }

    public string Id => Model.Id;

    private HostPlatform Platform => _settings.Platform.Platform;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowser), nameof(IsCommand))]
    public partial ProfileTargetKind TargetKind { get; set; }

    public bool IsBrowser => TargetKind == ProfileTargetKind.Browser;

    public bool IsCommand => TargetKind == ProfileTargetKind.Command;

    public IReadOnlyList<ProfileTargetKind> TargetKinds { get; } = [ProfileTargetKind.Browser, ProfileTargetKind.Command];

    /// <summary>Browsers for this machine's OS: detected ones first, then the rest of the catalog.</summary>
    public IReadOnlyList<BrowserChoice> BrowserChoices { get; }

    /// <summary>Browsers for Windows when the profile is shared with a Windows machine: the catalog.</summary>
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
    [NotifyPropertyChangedFor(nameof(HasIssues))]
    public partial IReadOnlyList<ConfigIssue> Issues { get; set; } = [];

    public bool HasIssues => Issues.Count > 0;

    /// <summary>Second line in the profile list.</summary>
    public string Summary
    {
        get
        {
            if (IsCommand)
            {
                var path = Platform == HostPlatform.MacOS ? MacOSCommandPath : WindowsCommandPath;
                return path.Length == 0 ? "Command" : $"Command: {Path.GetFileName(path)}";
            }
            var browser = PerOs
                ? $"{MacOSBrowser?.Title ?? "?"} on macOS, {WindowsBrowser?.Title ?? "?"} on Windows"
                : Browser?.Title ?? "No browser";
            var details = BrowserProfile?.Reference is { } reference ? PickerDisplay.DetailLine(new PickerCandidate { BrowserProfile = reference, Private = Private }) : Private ? "Private" : "";
            return details.Length == 0 ? browser : $"{browser} · {details}";
        }
    }

    /// <summary>Icon of the browser used on this machine.</summary>
    public byte[]? IconPng => _settings.Browsers.FirstOrDefault(b => b.Browser.Id == CurrentBrowserId)?.IconPng;

    private string? CurrentBrowserId => PerOs ? (Platform == HostPlatform.MacOS ? MacOSBrowser : WindowsBrowser)?.Id : Browser?.Id;

    /// <summary>Reads everything from <see cref="Model"/> again (after it was changed from outside this editor).</summary>
    public void Reload()
    {
        _syncing = true;
        try
        {
            var model = Model;
            Name = model.Name;
            TargetKind = model.Command is not null ? ProfileTargetKind.Command : ProfileTargetKind.Browser;

            var reference = model.Browser;
            PerOs = reference is not null && !reference.IsPlain;
            Browser = Find(BrowserChoices, reference?.Default ?? reference?.For(Platform));
            MacOSBrowser = Find(BrowserChoices, reference?.For(HostPlatform.MacOS));
            WindowsBrowser = Find(WindowsBrowserChoices, reference?.For(HostPlatform.Windows));
            Private = model.Private;
            ExtraArgs = string.Join("\n", model.ExtraArgs ?? []);

            MacOSCommandPath = model.Command?.MacOS?.Path ?? "";
            MacOSCommandArgs = string.Join("\n", model.Command?.MacOS?.Args ?? []);
            WindowsCommandPath = model.Command?.Windows?.Path ?? "";
            WindowsCommandArgs = string.Join("\n", model.Command?.Windows?.Args ?? []);

            BuildBrowserProfileChoices();
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
                FallbackChoices.Add(new ProfileChoice(fallback, $"{fallback} (missing)"));
            }
            Fallback = FallbackChoices.First(c => c.Id == Model.Fallback);
        }
        finally
        {
            _syncing = wasSyncing;
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

    partial void OnTargetKindChanged(ProfileTargetKind value) => WriteTarget();

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
            if (value)
            {
                MacOSBrowser = Browser;
                WindowsBrowser = Find(WindowsBrowserChoices, Browser?.Id);
            }
            else
            {
                Browser = Platform == HostPlatform.MacOS ? MacOSBrowser : Find(BrowserChoices, WindowsBrowser?.Id);
            }
        }
        finally
        {
            _syncing = false;
        }
        WriteTarget();
    }

    partial void OnMacOSBrowserChanged(BrowserChoice? value)
    {
        if (_syncing)
        {
            return;
        }
        if (Platform == HostPlatform.MacOS)
        {
            BrowserProfile = null;
            BuildBrowserProfileChoices();
        }
        WriteTarget();
    }

    partial void OnWindowsBrowserChanged(BrowserChoice? value) => WriteTarget();

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
        if (await _settings.Dialogs.PickFileAsync("Choose an application or executable") is not { } path)
        {
            return;
        }
        _syncing = true;
        try
        {
            if (path.TrimEnd('/').EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                // An app bundle is a folder, not an executable: let Launch Services open the URL with it.
                MacOSCommandPath = "/usr/bin/open";
                MacOSCommandArgs = $"-a\n{path.TrimEnd('/')}\n{{url}}";
            }
            else
            {
                MacOSCommandPath = path;
            }
        }
        finally
        {
            _syncing = false;
        }
        WriteTarget();
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

    private static List<string> Lines(string text)
        => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private void BuildBrowserProfileChoices()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            BrowserProfileChoices.Clear();
            BrowserProfileChoices.Add(NoBrowserProfile);
            BrowserProfileChoice selected = NoBrowserProfile;

            var detected = _settings.Browsers.FirstOrDefault(b => b.Browser.Id == CurrentBrowserId);
            var family = detected?.Browser.Family ?? (CurrentBrowserId is { } id ? BrowserCatalog.Find(id)?.Family : null) ?? BrowserFamily.Other;
            var profiles = detected?.Profiles ?? [];
            var stored = Model.BrowserProfile;
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
                        BrowserProfileChoices.Add(selected);
                    }
                    else
                    {
                        BrowserProfileChoices.Add(new BrowserProfileChoice(reference, title));
                    }
                }
            }

            if (stored is not null && match is null)
            {
                // Keep what is stored even when this machine does not have it: it may exist on another one.
                selected = new BrowserProfileChoice(stored, $"{stored.Name ?? stored.Email ?? stored.Directory} (not found on this computer)");
                BrowserProfileChoices.Add(selected);
            }
            BrowserProfile = selected;
        }
        finally
        {
            _syncing = wasSyncing;
        }
        OnPropertyChanged(nameof(HasBrowserProfiles));
    }

    private IReadOnlyList<BrowserChoice> BuildBrowserChoices(Profile model)
    {
        var choices = _settings.Browsers.Select(b => new BrowserChoice(b.Browser.Id, b.Browser.DisplayName, true, b.IconPng)).ToList();
        choices.AddRange(BrowserCatalog.Entries
            .Where(e => choices.All(c => c.Id != e.Id) && (Platform != HostPlatform.MacOS || e.MacBundleId is not null))
            .Select(e => new BrowserChoice(e.Id, $"{e.DisplayName} (not installed)", false, null)));
        AddStoredIds(choices, model.Browser?.Default, model.Browser?.For(Platform));
        return choices;
    }

    private static IReadOnlyList<BrowserChoice> BuildWindowsBrowserChoices(Profile model)
    {
        var choices = BrowserCatalog.Entries
            .Where(e => e.WindowsStartMenuKey is not null || e.WindowsStartMenuKeyPrefix is not null || e.WindowsKnownPaths.Count > 0)
            .Select(e => new BrowserChoice(e.Id, e.DisplayName, false, null))
            .ToList();
        AddStoredIds(choices, model.Browser?.Windows, model.Browser?.Default);
        return choices;
    }

    // Ids the configuration uses that are neither detected nor in the catalog (mac:/win: ids from another machine).
    private static void AddStoredIds(List<BrowserChoice> choices, params string?[] ids)
    {
        foreach (var id in ids.OfType<string>().Distinct().Where(id => choices.All(c => c.Id != id)))
        {
            choices.Add(new BrowserChoice(id, $"{id} (not installed)", false, null));
        }
    }

    private static BrowserChoice? Find(IReadOnlyList<BrowserChoice> choices, string? id) => id is null ? null : choices.FirstOrDefault(c => c.Id == id);
}
