using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core;
using Preferis.Core.Configuration;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>
/// Editor of what one profile opens on this machine instead of its shared target (<c>local.json</c>
/// <c>profileTargets</c>, docs/Configuration.md §3.1). Every edit is written to <see cref="Model"/> right away. One
/// machine has one OS, so there is nothing per OS here.
/// </summary>
public sealed partial class LocalTargetViewModel : ObservableObject
{
    private readonly ProfileViewModel _owner;
    private readonly SettingsViewModel _settings;
    private bool _syncing;

    internal LocalTargetViewModel(ProfileViewModel owner, SettingsViewModel settings, ProfileTarget model)
    {
        _owner = owner;
        _settings = settings;
        Model = model;
        BrowserChoices = ProfileViewModel.BuildBrowserChoices(settings.Browsers, Platform, model.Browser);

        _syncing = true;
        try
        {
            TargetKind = model.Command is not null ? ProfileTargetKind.Command : ProfileTargetKind.Browser;
            Browser = ProfileViewModel.Find(BrowserChoices, model.Browser);
            Private = model.Private;
            ExtraArgs = string.Join("\n", model.ExtraArgs ?? []);
            CommandPath = model.Command?.Path ?? "";
            CommandArgs = string.Join("\n", model.Command?.Args ?? []);
            BuildBrowserProfileChoices();
        }
        finally
        {
            _syncing = false;
        }
    }

    public ProfileTarget Model { get; }

    private HostPlatform Platform => _settings.Platform.Platform;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowser), nameof(IsCommand))]
    public partial ProfileTargetKind TargetKind { get; set; }

    public bool IsBrowser => TargetKind == ProfileTargetKind.Browser;

    public bool IsCommand => TargetKind == ProfileTargetKind.Command;

    public IReadOnlyList<ProfileTargetKind> TargetKinds { get; } = [ProfileTargetKind.Browser, ProfileTargetKind.Command];

    /// <summary>Browsers of this machine: detected ones first, then the rest of the catalog.</summary>
    public IReadOnlyList<BrowserChoice> BrowserChoices { get; }

    [ObservableProperty]
    public partial BrowserChoice? Browser { get; set; }

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
    public partial string CommandPath { get; set; } = "";

    /// <summary>One argument per line.</summary>
    [ObservableProperty]
    public partial string CommandArgs { get; set; } = "";

    /// <summary>What it opens, for the profile list.</summary>
    public string Summary => IsCommand
        ? ProfileViewModel.CommandSummary(CommandPath.Trim())
        : ProfileViewModel.BrowserSummary(Browser?.Title ?? L("No browser"), BrowserProfile?.Reference, Private);

    /// <summary>The browser it opens; null for a command.</summary>
    public string? BrowserId => IsBrowser ? Browser?.Id : null;

    partial void OnTargetKindChanged(ProfileTargetKind value) => Write();

    partial void OnBrowserChanged(BrowserChoice? value)
    {
        if (_syncing)
        {
            return;
        }
        // A browser profile of one browser means nothing to another.
        Model.BrowserProfile = null;
        BuildBrowserProfileChoices();
        Write();
    }

    partial void OnBrowserProfileChanged(BrowserProfileChoice? value) => Write();

    partial void OnPrivateChanged(bool value) => Write();

    partial void OnExtraArgsChanged(string value) => Write();

    partial void OnCommandPathChanged(string value) => Write();

    partial void OnCommandArgsChanged(string value) => Write();

    [RelayCommand]
    private async Task BrowseCommand()
    {
        var title = Platform == HostPlatform.MacOS ? L("Choose an application or executable") : L("Choose a program");
        if (await _settings.Dialogs.PickFileAsync(title) is not { } path)
        {
            return;
        }
        _syncing = true;
        try
        {
            var (commandPath, args) = Platform == HostPlatform.MacOS ? ProfileViewModel.MacOSCommandFor(path) : (path, null);
            CommandPath = commandPath;
            CommandArgs = args ?? CommandArgs;
        }
        finally
        {
            _syncing = false;
        }
        Write();
    }

    private void Write()
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
            // Kept even while empty, so that this stays a command; the issues say it has no program yet.
            model.Command = new CommandSpec { Path = CommandPath.Trim(), Args = ProfileViewModel.Lines(CommandArgs), ExtensionData = model.Command?.ExtensionData };
        }
        else
        {
            model.Command = null;
            model.Browser = Browser?.Id;
            model.BrowserProfile = BrowserProfile?.Reference;
            model.Private = Private;
            model.ExtraArgs = ProfileViewModel.Lines(ExtraArgs) is { Count: > 0 } args ? args : null;
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(BrowserId));
        _owner.OnLocalTargetChanged();
    }

    private void BuildBrowserProfileChoices()
    {
        var wasSyncing = _syncing;
        _syncing = true;
        try
        {
            BrowserProfile = ProfileViewModel.FillBrowserProfileChoices(BrowserProfileChoices, _settings.Browsers, Browser?.Id, Model.BrowserProfile);
        }
        finally
        {
            _syncing = wasSyncing;
        }
        OnPropertyChanged(nameof(HasBrowserProfiles));
    }
}
