using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Preferis.Core;
using Preferis.Core.Configuration;

namespace Preferis.Settings.ViewModels;

public enum PickerPosition
{
    Cursor,
    Center,
}

/// <summary>
/// The general page: where <c>config.json</c> lives, the registration as the default browser, the picker's behaviour
/// and language (shared, in <c>config.json</c>), and logging (this machine only, in <c>local.json</c>).
/// </summary>
public sealed partial class GeneralPageViewModel : PageViewModel
{
    private readonly bool _ready;

    public GeneralPageViewModel(SettingsViewModel settings)
        : base(settings)
    {
        var picker = settings.Config.Settings?.Picker;
        PickerPosition = picker?.Position == "center" ? PickerPosition.Center : PickerPosition.Cursor;
        AutoSelectSeconds = picker?.AutoSelectSeconds ?? 0;
        RememberChoiceDefault = picker?.RememberChoiceDefault ?? false;
        var language = settings.Config.Settings?.Language ?? "auto";
        Language = LanguageChoices.FirstOrDefault(l => l.Value == language) ?? LanguageChoices[0];

        var logging = settings.Local.Logging;
        LogLevel = LogLevelChoices.Contains(logging?.Level ?? "") ? logging!.Level : "error";
        IsHistoryEnabled = logging?.History ?? false;
        HistorySize = logging?.HistorySize ?? 50;

        RefreshRegistration();
        _ready = true;
    }

    public override string Title => "General";

    // ---- configuration file -----------------------------------------------------------------

    public string ConfigPath => Settings.ConfigPath;

    public bool IsAtDefaultLocation => string.Equals(Path.GetFullPath(Settings.ConfigPath), Path.GetFullPath(Settings.Loader.Paths.DefaultConfigPath), StringComparison.Ordinal);

    [RelayCommand]
    private void RevealConfig() => Settings.Platform.Reveal(Settings.ConfigExists ? ConfigPath : Path.GetDirectoryName(ConfigPath)!);

    /// <summary>"Move to cloud storage": the user picks a folder (e.g. under ~/Library/CloudStorage) and config.json moves there.</summary>
    [RelayCommand]
    private async Task MoveConfig()
    {
        var folder = await Settings.Dialogs.PickFolderAsync("Choose the folder to keep config.json in");
        if (folder is not null)
        {
            await MoveTo(Path.Combine(folder, "config.json"));
        }
    }

    /// <summary>For a second machine: use the config.json another machine already keeps in cloud storage.</summary>
    [RelayCommand]
    private async Task UseExistingConfig()
    {
        var file = await Settings.Dialogs.PickFileAsync("Choose an existing config.json");
        if (file is not null)
        {
            await Adopt(file);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveToDefaultLocation))]
    private Task MoveToDefaultLocation() => MoveTo(Settings.Loader.Paths.DefaultConfigPath);

    private bool CanMoveToDefaultLocation => !IsAtDefaultLocation;

    internal void OnConfigPathChanged()
    {
        OnPropertyChanged(nameof(ConfigPath));
        OnPropertyChanged(nameof(IsAtDefaultLocation));
        MoveToDefaultLocationCommand.NotifyCanExecuteChanged();
    }

    private async Task MoveTo(string target)
    {
        target = Path.GetFullPath(target);
        if (string.Equals(target, Path.GetFullPath(Settings.ConfigPath), StringComparison.Ordinal))
        {
            return;
        }
        if (Settings.ConfigUnreadable)
        {
            Settings.StatusMessage = "The current config.json cannot be read. Save a working configuration before moving it.";
            return;
        }
        // Moving writes the configuration shown here; take in what was saved to the file meanwhile, or stop on a conflict.
        Settings.CheckForExternalChanges();
        if (Settings.HasExternalChanges)
        {
            Settings.StatusMessage = "config.json changed on disk while it was being edited here. Reload or save first, then move it.";
            return;
        }

        try
        {
            if (File.Exists(target))
            {
                var answer = await Settings.Dialogs.AskAsync(
                    "A configuration already exists there",
                    $"{target} already exists, for example synced from another computer. Use it, or replace it with the configuration shown here?",
                    ["Use It", "Replace It", "Cancel"]);
                switch (answer)
                {
                    case 0:
                        await Adopt(target);
                        return;
                    case 1:
                        Settings.Relocate(target, overwrite: true);
                        break;
                    default:
                        return;
                }
            }
            else
            {
                Settings.Relocate(target, overwrite: false);
            }
            Settings.StatusMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Settings.StatusMessage = $"Could not move the configuration: {ex.Message}";
        }
    }

    private async Task Adopt(string path)
    {
        if (Settings.IsDirty
            && await Settings.Dialogs.AskAsync("Discard unsaved changes?", "Switching to another configuration file discards the changes made here.", ["Discard Changes", "Cancel"]) != 0)
        {
            return;
        }
        try
        {
            Settings.Adopt(path);
            Settings.StatusMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Settings.StatusMessage = ex.Message;
        }
    }

    // ---- default browser ----------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IsDefaultBrowser { get; private set; }

    [ObservableProperty]
    public partial string RegistrationText { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    public partial bool IsRegistering { get; private set; }

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task Register()
    {
        IsRegistering = true;
        try
        {
            var result = await Settings.Platform.RegisterAsync();
            Settings.StatusMessage = result.Succeeded ? null : result.Message;
        }
        finally
        {
            IsRegistering = false;
        }
        RefreshRegistration();
    }

    private bool CanRegister => Settings.Platform.CanRegister && !IsRegistering;

    [RelayCommand]
    private void OpenSystemSettings() => Settings.Platform.OpenDefaultBrowserSettings();

    [RelayCommand]
    private void RefreshRegistration()
    {
        var platform = Settings.Platform;
        if (!platform.CanRegister)
        {
            IsDefaultBrowser = false;
            RegistrationText = platform.Platform == HostPlatform.Windows
                ? "Registering needs Preferis Settings to run next to Preferis.exe (build it with build/Preferis.Build.proj)."
                : "Registering needs Preferis Settings to run from inside Preferis.app (build it with build/Preferis.Build.proj).";
            return;
        }

        var status = platform.GetRegistrationStatus();
        IsDefaultBrowser = status.IsDefault;
        var text = status.IsDefault ? "Preferis is the default browser."
            : status.CurrentDefault is { } current ? $"The default browser is {DisplayName(current)}."
            : "The default browser could not be determined.";
        RegistrationText = status.IsRegistered || status.IsDefault ? text : text + " Preferis is not registered with the system yet.";
    }

    // macOS reports the handler's path (/Applications/Safari.app), Windows its name ("Firefox 128.0").
    private static string DisplayName(string handler) => Path.IsPathRooted(handler) ? Path.GetFileNameWithoutExtension(handler) : handler;

    // ---- picker and language (config.json) ----------------------------------------------------

    private static readonly IReadOnlyList<EnumChoice<PickerPosition>> PickerPositionChoices =
    [
        new(PickerPosition.Cursor, "Near the pointer"),
        new(PickerPosition.Center, "In the center of the screen"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPickerPosition))]
    public partial PickerPosition PickerPosition { get; set; }

    public EnumChoice<PickerPosition> SelectedPickerPosition
    {
        get => PickerPositionChoices.First(p => p.Value == PickerPosition);
        // A drop-down may push null while its items are replaced; keep the value.
        set => PickerPosition = value?.Value ?? PickerPosition;
    }

    [ObservableProperty]
    public partial double AutoSelectSeconds { get; set; }

    [ObservableProperty]
    public partial bool RememberChoiceDefault { get; set; }

    public IReadOnlyList<EnumChoice<PickerPosition>> PickerPositions => PickerPositionChoices;

    public sealed record LanguageChoice(string Value, string Title) : Choice(Title);

    private static readonly IReadOnlyList<LanguageChoice> LanguageChoices =
    [
        new("auto", "Same as the system"),
        new("en", "English"),
        new("ja", "日本語"),
    ];

    public IReadOnlyList<LanguageChoice> Languages => LanguageChoices;

    [ObservableProperty]
    public partial LanguageChoice Language { get; set; }

    partial void OnPickerPositionChanged(PickerPosition value) => EditPicker(p => p.Position = value == PickerPosition.Center ? "center" : "cursor");

    partial void OnAutoSelectSecondsChanged(double value) => EditPicker(p => p.AutoSelectSeconds = Math.Max(0, value));

    partial void OnRememberChoiceDefaultChanged(bool value) => EditPicker(p => p.RememberChoiceDefault = value);

    partial void OnLanguageChanged(LanguageChoice value)
    {
        if (_ready && value is not null)
        {
            (Settings.Config.Settings ??= new AppSettings()).Language = value.Value;
            Settings.MarkChanged();
        }
    }

    // Settings are created on the first change only, so that opening and closing the page changes nothing.
    private void EditPicker(Action<PickerSettings> edit)
    {
        if (_ready)
        {
            var settings = Settings.Config.Settings ??= new AppSettings();
            edit(settings.Picker ??= new PickerSettings());
            Settings.MarkChanged();
        }
    }

    // ---- logging (local.json) -----------------------------------------------------------------

    private static readonly IReadOnlyList<string> LogLevelChoices = ["off", "error", "info", "trace"];

    public IReadOnlyList<string> LogLevels => LogLevelChoices;

    [ObservableProperty]
    public partial string LogLevel { get; set; }

    [ObservableProperty]
    public partial bool IsHistoryEnabled { get; set; }

    [ObservableProperty]
    public partial int HistorySize { get; set; }

    public string LogDirectory => Settings.Loader.Paths.LogDirectory;

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(LogDirectory);
        Settings.Platform.Reveal(LogDirectory);
    }

    partial void OnLogLevelChanged(string value) => EditLogging(l => l.Level = value);

    partial void OnIsHistoryEnabledChanged(bool value) => EditLogging(l => l.History = value);

    partial void OnHistorySizeChanged(int value) => EditLogging(l => l.HistorySize = Math.Clamp(value, 1, 10_000));

    private void EditLogging(Action<LoggingSettings> edit)
    {
        if (_ready)
        {
            edit(Settings.Local.Logging ??= new LoggingSettings());
            Settings.MarkChanged();
        }
    }

    public string Version => typeof(GeneralPageViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public string ConfigPathHint => string.Format(CultureInfo.InvariantCulture, "Keep config.json in a synced folder (OneDrive, Google Drive, iCloud Drive...) to share it with other computers. {0} stays on this computer and remembers where it is.", Path.GetFileName(Settings.Loader.Paths.LocalConfigPath));
}
