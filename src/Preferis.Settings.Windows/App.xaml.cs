using Microsoft.UI.Xaml;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;
using static Preferis.Settings.Localization.SettingsText;

namespace Preferis.Settings.Windows;

public partial class App : Application
{
    private SettingsViewModel? _settings;
    private SettingsWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"Unhandled exception: {e.Exception}");
            // An edit that throws should not take the unsaved edits down with the window; say so and carry on.
            if (_settings is not null)
            {
                e.Handled = true;
                _settings.StatusMessage = L("Something went wrong: {0}", e.Exception.Message);
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var loader = SettingsConfigLoader.Create();
        SettingsConfigLoader.UseConfiguredLanguage(loader);
        _settings = new SettingsViewModel(loader, new WindowsSettingsPlatform(loader.LoadLocal()), new WinUIDialogService(() => _window?.Window), new DispatcherSaveScheduler());
        _window = new SettingsWindow(_settings);

        if (Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT") is { Length: > 0 } directory)
        {
            Snapshots.TakeAndExit(_window, _settings, directory);
            return;
        }
        _window.Show();
    }
}
