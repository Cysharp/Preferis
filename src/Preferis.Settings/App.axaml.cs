using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Preferis.Core.Configuration;
using Preferis.Settings.Services;
using Preferis.Settings.ViewModels;
using Preferis.Settings.Views;

namespace Preferis.Settings;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("The Windows settings platform arrives in M4 (docs/Design.md §14).");
            }

            var loader = ConfigLoader.ForCurrentMachine();
            var window = new MainWindow();
            var settings = new SettingsViewModel(loader, new MacSettingsPlatform(loader.LoadLocal()), new AvaloniaDialogService(() => window));
            window.DataContext = settings;
            desktop.MainWindow = window;

            if (Environment.GetEnvironmentVariable("PREFERIS_SETTINGS_SNAPSHOT") is { Length: > 0 } directory)
            {
                Snapshots.TakeAndExit(window, settings, directory, desktop);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
