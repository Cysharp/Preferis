using Avalonia.Controls;
using Preferis.Settings.ViewModels;

namespace Preferis.Settings.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        // Coming back to the window is when a change made elsewhere (Router, sync client) should show up.
        Activated += (_, _) => (DataContext as SettingsViewModel)?.CheckForExternalChanges();
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || DataContext is not SettingsViewModel settings || !settings.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        if (await settings.CanCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
