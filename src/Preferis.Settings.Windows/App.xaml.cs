using Microsoft.UI.Xaml;
using Preferis.Core.Configuration;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;

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
                _settings.StatusMessage = $"Something went wrong: {e.Exception.Message}";
            }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var loader = ConfigLoader.ForCurrentMachine();
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
