using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Preferis.Settings.Services;

/// <summary>Native file pickers through Avalonia's storage provider and a small message window for questions.</summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Func<Window> _owner;

    public AvaloniaDialogService(Func<Window> owner)
    {
        _owner = owner;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await _owner().StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(string title)
    {
        var files = await _owner().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<int> AskAsync(string title, string message, IReadOnlyList<string> buttons)
    {
        var dialog = new Window
        {
            Title = "",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Content = buttons[i],
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                // The first button is the suggested answer; "Cancel" answers to Esc.
                IsDefault = i == 0,
                IsCancel = buttons[i] == "Cancel",
            };
            if (i == 0)
            {
                button.Classes.Add("accent");
            }
            button.Click += (_, _) => dialog.Close(index);
            row.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                row,
            },
        };

        var result = await dialog.ShowDialog<int?>(_owner());
        return result ?? -1;
    }
}
