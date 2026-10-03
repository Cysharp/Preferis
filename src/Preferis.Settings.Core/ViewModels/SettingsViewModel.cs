using System.Text.Json;
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
    private readonly ConfigLoader _loader;
    private readonly ISaveScheduler _saves;

    // config.json as it was on disk when we last read or wrote it (null: no file), to notice outside changes.
    private string? _diskText;
    private string _savedConfigJson = "";
    private string _savedLocalJson = "";
    private bool _loading;

    public SettingsViewModel(ConfigLoader loader, ISettingsPlatform platform, IDialogService dialogs, ISaveScheduler saves)
    {
        _loader = loader;
        _saves = saves;
        Platform = platform;
        Dialogs = dialogs;
        Browsers = platform.DetectBrowsers();
        Load();
    }

    public ISettingsPlatform Platform { get; }

    public IDialogService Dialogs { get; }

    public ConfigLoader Loader => _loader;

    /// <summary>Browsers installed on this machine, detected once per session (refreshable from the profiles page).</summary>
    public IReadOnlyList<DetectedBrowserInfo> Browsers { get; private set; }

    public PreferisConfig Config { get; private set; } = PreferisConfig.CreateDefault();

    public LocalConfig Local { get; private set; } = new();

    [ObservableProperty]
    public partial string ConfigPath { get; private set; } = "";

    [ObservableProperty]
    public partial bool ConfigExists { get; private set; }

    /// <summary>True when <c>config.json</c> exists but could not be parsed: saving replaces it, moving it is refused.</summary>
    public bool ConfigUnreadable { get; private set; }

    private bool LocalUnreadable { get; set; }

    private bool ConfigEdited => ConfigSerializer.Serialize(Config) != _savedConfigJson;

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
    internal void MarkChanged()
    {
        if (_loading)
        {
            return;
        }
        var configEdited = ConfigEdited;
        var localEdited = ConfigSerializer.SerializeLocal(Local) != _savedLocalJson;
        IsDirty = configEdited || localEdited;
        Issues = [.. ConfigValidator.Validate(Config), .. ProfileAvailability.Check(Config, Local, Browsers, Platform.Platform)];
        Profiles.UpdateIssues(Issues);
        Rules.UpdateIssues(Issues);
        // Held config.json edits wait for a decision; asking for a save again would only hold them again.
        if ((configEdited && !HasExternalChanges && !IsHoldingEditsForUnreadableFile) || localEdited)
        {
            _saves.Schedule(() => SaveCore(force: false));
        }
    }

    /// <summary>Saves the held edits over <c>config.json</c>, whether it changed elsewhere or could not be read.</summary>
    [RelayCommand]
    private void KeepMine() => SaveCore(force: true);

    /// <summary>Drops the held edits of <c>config.json</c> and shows the file as it is now.</summary>
    [RelayCommand]
    private void UseTheirs() => Load(keepLocalEdits: true);

    /// <summary>Saves now, for a page that needs the file on disk to be current (opening a URL for real). False when it could not.</summary>
    internal bool SaveNow() => SaveCore(force: false);

    /// <summary>
    /// Called when the window becomes active (and before moving the file): a <c>config.json</c> changed elsewhere is
    /// reloaded when it has no edits here, and flagged otherwise. Pending <c>local.json</c> edits survive the reload.
    /// </summary>
    public void CheckForExternalChanges()
    {
        if (ReadDisk() == _diskText)
        {
            return;
        }
        if (ConfigEdited)
        {
            HasExternalChanges = true;
        }
        else
        {
            Load(keepLocalEdits: true);
        }
    }

    /// <summary>
    /// Saves the edits still waiting before the window closes, and asks only when they cannot be saved. Returns false
    /// when the window should stay open.
    /// </summary>
    public async Task<bool> CanCloseAsync()
    {
        if (!IsDirty || SaveCore(force: false))
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
            return SaveCore(force: true);
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
    /// <exception cref="IOException">Writing failed, or <c>config.json</c> changed elsewhere while edited here.</exception>
    internal void Relocate(string target, bool overwrite)
    {
        // Under the lock, as saving is: a rule the Router remembered in between would otherwise stay in the old file, or
        // bring it back after the move deleted it. The page checked for changes before asking, maybe a while ago.
        using (_loader.Lock())
        {
            CheckForExternalChanges();
            if (HasExternalChanges)
            {
                throw new IOException(L("config.json changed on disk while it was being edited here. Reload or save first, then move it."));
            }
            _loader.RelocateConfig(Config, ConfigPath, target, overwrite);
        }
        var json = ConfigSerializer.Serialize(Config);
        _savedConfigJson = json;
        _diskText = json;
        // local.json now points at the new place; other local edits are saved as usual after the pause.
        var onDisk = _loader.LoadLocal();
        Local.ConfigPath = onDisk.ConfigPath;
        _savedLocalJson = ConfigSerializer.SerializeLocal(onDisk);
        ConfigPath = Path.GetFullPath(target);
        ConfigExists = true;
        HasExternalChanges = false;
        General.OnConfigPathChanged();
        MarkChanged();
    }

    /// <summary>Switches to a configuration that already exists elsewhere and reloads everything from it.</summary>
    internal void Adopt(string path)
    {
        _loader.UseExistingConfig(path);
        Load();
    }

    /// <summary>Writes what was edited. False when edits of <c>config.json</c> are held back or writing failed.</summary>
    private bool SaveCore(bool force)
    {
        var configJson = ConfigSerializer.Serialize(Config);
        var configEdited = configJson != _savedConfigJson;
        bool diskChanged;
        var holdConfig = false;
        try
        {
            // The Router remembers a choice by reading, changing and writing config.json under the same lock, so it
            // cannot write between this comparison and the write below, which would replace its rule unseen.
            using var exclusive = _loader.Lock();
            diskChanged = ReadDisk() != _diskText;
            // Only edits of config.json can conflict with the file on disk; local.json belongs to this machine alone and
            // is saved either way.
            if (configEdited && !force)
            {
                if (diskChanged)
                {
                    HasExternalChanges = holdConfig = true;
                }
                else if (ConfigUnreadable)
                {
                    IsHoldingEditsForUnreadableFile = holdConfig = true;
                }
            }

            if (configEdited && !holdConfig)
            {
                _loader.Save(Config, ConfigPath);
                _savedConfigJson = configJson;
                _diskText = configJson;
                ConfigUnreadable = false;
                IsHoldingEditsForUnreadableFile = false;
                HasExternalChanges = false;
            }
            var localJson = ConfigSerializer.SerializeLocal(Local);
            if (localJson != _savedLocalJson)
            {
                _loader.SaveLocal(Local);
                _savedLocalJson = localJson;
                LocalUnreadable = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Also when another process held the lock too long. The edits stay waiting (IsDirty) and go with the next
            // edit, or when the window closes.
            StatusMessage = L("Could not save: {0}", ex.Message);
            return false;
        }

        StatusMessage = null;
        if (!configEdited && diskChanged)
        {
            // Nothing of ours was in config.json: show what someone else saved there.
            Load(keepLocalEdits: true);
            return true;
        }

        ConfigExists = File.Exists(ConfigPath);
        if (!ConfigUnreadable && !LocalUnreadable)
        {
            LoadError = null;
        }
        MarkChanged();
        return !holdConfig;
    }

    private void Load(bool keepLocalEdits = false)
    {
        var current = SelectedPage is null ? 0 : Math.Max(0, Pages.ToList().IndexOf(SelectedPage));
        var result = _loader.Load();

        _loading = true;
        Config = result.Config;
        ConfigPath = result.ConfigPath;
        ConfigExists = result.Exists;
        ConfigUnreadable = result.Exists && !IsParsable(result.ConfigPath, ConfigSerializer.Deserialize);
        if (!keepLocalEdits)
        {
            Local = result.Local;
            _savedLocalJson = ConfigSerializer.SerializeLocal(Local);
            LocalUnreadable = File.Exists(_loader.Paths.LocalConfigPath) && !IsParsable(_loader.Paths.LocalConfigPath, ConfigSerializer.DeserializeLocal);
        }
        // Profiles renamed on another machine: this machine's targets follow them to their new ids (saved as an edit).
        LocalProfileTargets.MigrateKeys(Config, Local);
        LoadError = result.Error;
        HasExternalChanges = false;
        IsHoldingEditsForUnreadableFile = false;
        _diskText = ReadDisk();
        _savedConfigJson = ConfigSerializer.Serialize(Config);

        Profiles = new ProfilesPageViewModel(this);
        Rules = new RulesPageViewModel(this);
        // The test page holds no configuration of its own (it routes with Config when asked), and keeping it keeps
        // what it shows, e.g. the result of opening a URL whose picker choice was remembered and caused this reload.
        Test ??= new TestPageViewModel(this);
        General = new GeneralPageViewModel(this);
        _loading = false;

        ReplacePages(null);
        var unchanged = Pages[current] == SelectedPage;
        SelectedPage = Pages[current];
        if (unchanged)
        {
            // Still the same page object, so it was not activated by the change: refresh it with the reloaded configuration.
            SelectedPage.Activate();
        }
        MarkChanged();
    }

    private void ReplacePages(PageViewModel? select)
    {
        Pages = [General, Profiles, Rules, Test];
        if (select is not null)
        {
            SelectedPage = select;
        }
    }

    private string? ReadDisk()
    {
        try
        {
            return File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable right now (e.g. a sync client holds it): treat as unchanged rather than guess.
            return _diskText;
        }
    }

    private static bool IsParsable<T>(string path, Func<string, T> parse)
    {
        try
        {
            parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
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
