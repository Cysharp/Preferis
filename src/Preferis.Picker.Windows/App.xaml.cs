using Microsoft.UI.Xaml;
using Preferis.Core.Picker;

namespace Preferis.Picker.Windows;

public partial class App : Application
{
    private readonly PickerRequest _request;
    private readonly CandidateImages _images;
    private PickerWindow? _window;

    internal App(PickerRequest request, CandidateImages images)
    {
        _request = request;
        _images = images;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Without an answer the Router falls back right away (docs/Design.md §8); a hidden, hung picker would block it.
            Console.Error.WriteLine($"The picker failed: {e.Exception}");
            Environment.Exit(1);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Program.Mark("winui");
        try
        {
            _window = new PickerWindow(_request, _images, Program.Answer, Exit);
            _window.Show();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"The picker could not be shown: {ex}");
            Environment.Exit(1);
        }
    }
}
