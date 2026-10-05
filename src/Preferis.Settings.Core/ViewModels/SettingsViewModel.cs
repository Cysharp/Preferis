using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core.Configuration;
using Preferis.Settings.Services;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.ViewModels;

/// <summary>
/// One editing session over <c>config.json</c> and <c>local.json</c>. The pages edit <see cref="Config"/> and
/// <see cref="Local"/> in place (so properties this version does not know survive) and report each edit through
/// <see cref="MarkChanged"/>; edits are saved after a short pause (<see cref="ISaveScheduler"/>), as settings on macOS
/// are. A <c>config.json</c> changed by someone else meanwhile (the Router's "remember", a sync client), or one that
/// could not be read, is never overwritten without asking.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private readonly ISaveScheduler _saves;

    // Every edit validates the whole configuration; most leave the patterns as they were (and reloading the file
    // usually does too), so their errors are looked up rather than checked again.
    private readonly PatternErrorCache _patterns;

    private bool _loading;

    public SettingsViewModel(ConfigLoader loader, ISettingsPlatform platform, IDialogService dialogs, ISaveScheduler saves)
        : this(loader, platform, dialogs, saves, new PatternErrorCache())
    {
    }

    /// <param name="patterns">Where validation looks up the pattern errors (the tests count what it checks).</param>
    internal SettingsViewModel(ConfigLoader loader, ISettingsPlatform platform, IDialogService dialogs, ISaveScheduler saves, PatternErrorCache patterns)
    {
        _session = new SettingsSession(loader);
        _saves = saves;
        _patterns = patterns;
        Platform = platform;
        Dialogs = dialogs;
        Browsers = platform.DetectBrowsers();
        _session.ModelsReplaced += () => ApplySession(scheduleSave: false);
        ApplySession();
    }

    public ISettingsPlatform Platform { get; }

    public IDialogService Dialogs { get; }

    public ConfigLoader Loader => _session.Loader;

    /// <summary>Browsers installed on this machine, detected once per session (refreshable from the profiles page).</summary>
    public IReadOnlyList<DetectedBrowserInfo> Browsers { get; private set; }

    public PreferisConfig Config { get; private set; } = PreferisConfig.CreateDefault();

    public LocalConfig Local { get; private set; } = new();

    [ObservableProperty]
    public partial string ConfigPath { get; private set; } = "";

    [ObservableProperty]
    public partial bool ConfigExists { get; private set; }

    /// <summary>
    /// True when <c>config.json</c> exists but could not be read as a configuration (malformed JSON, or null where a value
    /// is needed): saving replaces it, moving it is refused.
    /// </summary>
    public bool ConfigUnreadable { get; private set; }

    /// <summary>Includes a write in flight even if the user has just undone the edit that started it.</summary>
    public bool HasPendingChanges => IsDirty || _session.IsBusy;

    /// <summary>Edits that are not on disk yet: waiting for the pause, or held back (see <see cref="HasExternalChanges"/>).</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; private set; }

    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    /// <summary><c>config.json</c> changed elsewhere while edits were waiting: they are held until Keep Mine or Use Theirs.</summary>
    [ObservableProperty]
    public partial bool HasExternalChanges { get; private set; }

    /// <summary>
    /// Edits of a <c>config.json</c> that could not be read are held until Keep Mine, which replaces the file: whatever
    /// was in it may still matter to the person who wrote it.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHoldingEditsForUnreadableFile { get; private set; }

    /// <summary>The outcome of the last operation that failed (saving, moving the file, registering), for the status bar.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IssueSummary))]
    public partial IReadOnlyList<ConfigIssue> Issues { get; private set; } = [];

    [ObservableProperty]
    public partial ProfilesPageViewModel Profiles { get; private set; } = null!;

    [ObservableProperty]
    public partial RulesPageViewModel Rules { get; private set; } = null!;

    [ObservableProperty]
    public partial TestPageViewModel Test { get; private set; } = null!;

    [ObservableProperty]
    public partial GeneralPageViewModel General { get; private set; } = null!;

    [ObservableProperty]
    public partial IReadOnlyList<PageViewModel> Pages { get; private set; } = [];

    [ObservableProperty]
    public partial PageViewModel? SelectedPage { get; set; }

    /// <summary>"2 errors, 1 warning"; empty when the configuration is clean.</summary>
    public string IssueSummary
    {
        get
        {
            var errors = Issues.Count(i => i.Severity == IssueSeverity.Error);
            var warnings = Issues.Count - errors;
            var parts = new List<string>();
            if (errors > 0)
            {
                parts.Add(errors == 1 ? L("1 error") : L("{0} errors", errors));
            }
            if (warnings > 0)
            {
                parts.Add(warnings == 1 ? L("1 warning") : L("{0} warnings", warnings));
            }
            return string.Join(L(", "), parts);
        }
    }

    partial void OnSelectedPageChanged(PageViewModel? value) => value?.Activate();

    /// <summary>Called by the pages after every edit of <see cref="Config"/> or <see cref="Local"/>; schedules the save.</summary>
    internal void MarkChanged(bool scheduleSave = true)
    {
        if (_loading)
        {
            return;
        }
        IsDirty = _session.IsDirty;
        Issues = [.. ConfigValidator.Validate(Config, _patterns), .. ProfileAvailability.Check(Config, Local, Browsers, Platform.Platform)];
        Profiles.UpdateIssues(Issues);
        Rules.UpdateIssues(Issues);
        // Held config.json edits wait for a decision; asking for a save again would only hold them again.
        if (scheduleSave && _session.CanAutoSave)
        {
            _saves.Schedule(() => SaveCoreAsync(force: false));
        }
    }

    /// <summary>Saves the held edits over <c>config.json</c>, whether it changed elsewhere or could not be read.</summary>
    [RelayCommand]
    private Task KeepMine() => SaveCoreAsync(force: true);

    /// <summary>Drops the held edits of <c>config.json</c> and shows the file as it is now.</summary>
    [RelayCommand]
    private async Task UseTheirs()
    {
        await _session.UseTheirsAsync();
        StatusMessage = _session.SaveError;
        ApplySession();
    }

    /// <summary>Saves now, for a page that needs the file on disk to be current (opening a URL for real). False when it could not.</summary>
    internal async Task<bool> SaveNowAsync()
    {
        do
        {
            if (!await SaveCoreAsync(force: false))
            {
                return false;
            }
        }
        while (IsDirty);
        return true;
    }

    /// <summary>
    /// Called when the window becomes active (and before moving the file): a <c>config.json</c> changed elsewhere is
    /// reloaded when it has no edits here, and flagged otherwise. Pending <c>local.json</c> edits survive the reload.
    /// </summary>
    public void CheckForExternalChanges()
    {
        _session.CheckForExternalChanges();
        ApplySession();
    }

    /// <summary>
    /// Saves the edits still waiting before the window closes, and asks only when they cannot be saved. Returns false
    /// when the window should stay open.
    /// </summary>
    public async Task<bool> CanCloseAsync()
    {
        if (!HasPendingChanges || await SaveNowAsync())
        {
            return true;
        }
        var held = HasExternalChanges || IsHoldingEditsForUnreadableFile;
        var (title, message) = HasExternalChanges
            ? (L("config.json was changed elsewhere while you were editing."), L("Keep your changes to save them over it, or discard them to leave the file as it is."))
            : IsHoldingEditsForUnreadableFile
                ? (L("config.json could not be read."), L("Keep your changes to replace the file with them, or discard them to leave the file as it is."))
                : (L("Your changes could not be saved."), StatusMessage ?? "");
        // Cancel comes last and Discard My Changes right before it, with Keep Mine first when there is a choice.
        string[] buttons = held ? [L("Keep Mine"), L("Discard My Changes"), L("Cancel")] : [L("Discard My Changes"), L("Cancel")];
        var answer = await Dialogs.AskAsync(title, message, buttons);
        if (held && answer == 0)
        {
            return await SaveCoreAsync(force: true) && await SaveNowAsync();
        }
        return answer == buttons.Length - 2;
    }

    /// <summary>Detects the installed browsers again (after installing one) and rebuilds the pages that show them.</summary>
    internal void RedetectBrowsers()
    {
        Browsers = Platform.DetectBrowsers();
        var selected = Profiles.SelectedProfile?.Id;
        var current = SelectedPage;
        Profiles = new ProfilesPageViewModel(this);
        Profiles.SelectedProfile = Profiles.Profiles.FirstOrDefault(p => p.Id == selected) ?? Profiles.SelectedProfile;
        ReplacePages(current is ProfilesPageViewModel ? Profiles : current);
        MarkChanged();
    }

    /// <summary>"Move to cloud storage": writes the configuration being edited (including edits still waiting) to <paramref name="target"/>.</summary>
    /// <exception cref="IOException">
    /// Writing failed, or <c>config.json</c> changed elsewhere while edited here, or can no longer be read.
    /// </exception>
    internal async Task<string?> RelocateAsync(string target, bool overwrite)
    {
        try
        {
            return await _session.RelocateAsync(target, overwrite);
        }
        finally
        {
            ApplySession();
            General.OnConfigPathChanged();
        }
    }

    /// <summary>Switches to an existing configuration after any in-flight save has finished.</summary>
    internal async Task AdoptAsync(string path)
    {
        await _session.AdoptAsync(path);
        // ApplySession ran synchronously at the model switch; any input while saving now edits the new file.
        if (!await SaveNowAsync())
        {
            throw new IOException(StatusMessage);
        }
    }

    private async Task<bool> SaveCoreAsync(bool force)
    {
        var saved = await _session.SaveAsync(force);
        StatusMessage = _session.SaveError;
        ApplySession(scheduleSave: saved);
        return saved;
    }

    /// <summary>Refreshes display state; only replacing the edited model replaces its page view models.</summary>
    private void ApplySession(bool scheduleSave = true)
    {
        var replaced = !ReferenceEquals(Config, _session.Config);
        var current = SelectedPage is null ? 0 : Math.Max(0, Pages.ToList().IndexOf(SelectedPage));
        _loading = true;
        Config = _session.Config;
        Local = _session.Local;
        ConfigPath = _session.ConfigPath;
        ConfigExists = _session.ConfigExists;
        ConfigUnreadable = _session.ConfigUnreadable;
        LoadError = _session.LoadError;
        HasExternalChanges = _session.HasExternalChanges;
        IsHoldingEditsForUnreadableFile = _session.IsHoldingEditsForUnreadableFile;
        if (replaced)
        {
            Profiles = new ProfilesPageViewModel(this);
            Rules = new RulesPageViewModel(this);
            // Keep the test result across reloads caused by the Router remembering a picker choice.
            Test ??= new TestPageViewModel(this);
            General = new GeneralPageViewModel(this);
        }
        _loading = false;
        if (replaced)
        {
            ReplacePages(null);
            var unchanged = Pages[current] == SelectedPage;
            SelectedPage = Pages[current];
            if (unchanged)
            {
                SelectedPage!.Activate();
            }
        }
        MarkChanged(scheduleSave);
    }

    private void ReplacePages(PageViewModel? select)
    {
        Pages = [General, Profiles, Rules, Test];
        if (select is not null)
        {
            SelectedPage = select;
        }
    }

}

/// <summary>A page of the settings window.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(SettingsViewModel settings)
    {
        Settings = settings;
    }

    public SettingsViewModel Settings { get; }

    public abstract string Title { get; }

    /// <summary>Called whenever the page is shown, so it can pick up edits made on other pages.</summary>
    public virtual void Activate()
    {
    }
}
