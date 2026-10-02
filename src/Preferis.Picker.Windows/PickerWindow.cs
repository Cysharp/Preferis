using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Ellipse = Microsoft.UI.Xaml.Shapes.Ellipse;
using Preferis.Core.Localization;
using Preferis.Core.Picker;
using Preferis.Platform.Windows;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using static Preferis.Core.Localization.CoreText;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;

namespace Preferis.Picker.Windows;

/// <summary>
/// The picker window. Renders <see cref="PickerSession"/> and forwards input to it; the session decides
/// (docs/Design.md §9.2, §11). Built in code like the AppKit picker. Looks like a Windows 11 dialog without a title bar:
/// the host as a large header and the candidates as a list above, the buttons in the dialog's footer below.
/// </summary>
internal sealed class PickerWindow
{
    private const double ContentWidth = 520;

    // ContentDialogPadding of WinUI's own dialogs.
    private const double DialogPadding = 24;
    private const double RowHeight = 56;
    private const int MaxVisibleRows = 6;

    private readonly PickerSession _session;
    private readonly Action<PickerResponse> _answer;
    private readonly Action _quit;
    private readonly CandidateImages _images;
    private readonly Window _window;
    private readonly Grid _root;
    private readonly ListView _list;
    private readonly Button _open;
    private readonly FontIcon _copyIcon;
    private DispatcherQueueTimer? _timer;
    private bool _answered;
    private bool _closing;

    /// <param name="images">The candidates' icons, loading since the process started.</param>
    /// <param name="answer">Delivers the answer to the Router.</param>
    /// <param name="quit">Ends the process once the window is gone.</param>
    public PickerWindow(PickerRequest request, CandidateImages images, Action<PickerResponse> answer, Action quit)
    {
        _session = new PickerSession(request, TimeProvider.System);
        _images = images;
        _answer = answer;
        _quit = quit;

        // No title bar, like Windows 11's own app chooser. Changing the frame costs about 65 ms however it is done (also
        // with ExtendsContentIntoTitleBar), a quarter of the start-up; docs/Platform.md §5.
        _window = new Window { Title = "Preferis" };
        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsResizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        _window.AppWindow.SetPresenter(presenter);
        _window.AppWindow.Closing += (_, _) =>
        {
            // Alt+F4 and the like count as cancelling.
            if (!_answered)
            {
                _closing = true;
                Answer(_session.Cancel());
            }
        };
        Win32.RoundCorners(Handle);

        // Laid out like a ContentDialog, with its colors: the content on the lighter top overlay, a separator, and the
        // buttons on the dialog background below.
        var content = new Grid
        {
            Padding = new Thickness(DialogPadding, 20, DialogPadding, 16),
            RowSpacing = 12,
            Background = Brush("ContentDialogTopOverlay"),
            BorderBrush = Brush("ContentDialogSeparatorBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        var rows = new List<UIElement>();

        _copyIcon = new FontIcon { Glyph = Glyphs.Copy, FontSize = 16 };
        rows.Add(BuildHeader(request.Url));
        if (request.Message is { Length: > 0 } message)
        {
            rows.Add(new InfoBar { Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = false, Message = message });
        }

        _list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = MaxVisibleRows * RowHeight,
            Margin = new Thickness(-12, 0, -12, 0),
        };
        for (var i = 0; i < request.Candidates.Count; i++)
        {
            _list.Items.Add(BuildRow(request.Candidates[i], i));
        }
        _list.SelectionChanged += (_, _) => OnSelectionChanged();
        rows.Add(_list);
        if (BuildRemember(request) is { } remember)
        {
            rows.Add(remember);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)rows[i], i);
            content.Children.Add(rows[i]);
        }

        _open = new Button
        {
            Content = L("Open"),
            Style = Resource<Style>("AccentButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = request.Candidates.Count > 0,
        };
        _open.Click += (_, _) => Answer(_session.Confirm());
        var cancel = new Button { Content = L("Cancel"), HorizontalAlignment = HorizontalAlignment.Stretch };
        cancel.Click += (_, _) => Answer(_session.Cancel());
        // Windows puts the primary action first, and a dialog's buttons share the width equally.
        var footer = Columns(8, (_open, new GridLength(1, GridUnitType.Star)), (cancel, new GridLength(1, GridUnitType.Star)));
        footer.Padding = new Thickness(DialogPadding);

        _root = new Grid { Background = Brush("ContentDialogBackground") };
        if (UiLanguages.Current == UiLanguage.Japanese)
        {
            // Text falls back to the fonts of this language: kanji in a Chinese font on an English Windows otherwise.
            _root.Language = "ja-JP";
        }
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Children.Add(content);
        Grid.SetRow(footer, 1);
        _root.Children.Add(footer);

        _root.PreviewKeyDown += OnKeyDown;
        _root.PointerMoved += (_, _) => _session.NoteActivity();
        _root.PointerWheelChanged += (_, _) => _session.NoteActivity();
        _root.Loaded += (_, _) => OnLoaded(request.Options.Position);

        _window.Content = _root;
        Program.Mark("built");
    }

    private IntPtr Handle => Microsoft.UI.Win32Interop.GetWindowFromWindowId(_window.AppWindow.Id);

    public void Show()
    {
        // Laid out off screen first: the window can only be sized to its content once that content has loaded.
        _window.AppWindow.Move(new PointInt32(-32000, -32000));
        _window.Activate();
    }

    private void OnLoaded(string position)
    {
        Program.Mark("loaded");
        Place(position);
        Win32.SetForegroundWindow(Handle);
        if (_session.SelectedIndex >= 0)
        {
            _list.SelectedIndex = _session.SelectedIndex;
            (_list.ContainerFromIndex(_session.SelectedIndex) as ListViewItem)?.Focus(FocusState.Programmatic);
        }
        Program.Mark(Win32.GetForegroundWindow() == Handle ? "shown" : "shown (not in front)");

        if (_session.AutoSelectRemaining is not null)
        {
            UpdateOpenButton();
            _timer = _window.DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
        }
        if (Environment.GetEnvironmentVariable("PREFERIS_PICKER_SNAPSHOT") is { Length: > 0 } snapshot)
        {
            var delay = _window.DispatcherQueue.CreateTimer();
            delay.Interval = TimeSpan.FromMilliseconds(500);
            delay.IsRepeating = false;
            delay.Tick += async (_, _) => await Snapshot(snapshot);
            delay.Start();
        }
    }

    // Near the pointer (centered on it horizontally, the top just above it) or in the middle of the pointer's screen,
    // inside the screen's work area either way.
    private void Place(string position)
    {
        var cursor = Win32.CursorPosition();
        var scale = Win32.ScaleAt(cursor);
        _root.Measure(new Size(ContentWidth, double.PositiveInfinity));
        var width = (int)Math.Ceiling(ContentWidth * scale);
        var height = (int)Math.Ceiling(_root.DesiredSize.Height * scale);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).WorkArea;

        var (x, y) = position == "center"
            ? (area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2)
            : (cursor.X - width / 2, cursor.Y - (int)(16 * scale));
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));

        _window.AppWindow.ResizeClient(new SizeInt32(width, height));
        // Without a title bar ResizeClient still converts as if there were one, and the window came out a title bar's
        // height too tall (45 px at 150 %), all of it below the buttons. Take off whatever it added.
        var client = _window.AppWindow.ClientSize;
        if (client.Width != width || client.Height != height)
        {
            _window.AppWindow.ResizeClient(new SizeInt32(2 * width - client.Width, 2 * height - client.Height));
        }
        _window.AppWindow.Move(new PointInt32(x, y));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        _session.NoteActivity();
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var focused = FocusManager.GetFocusedElement(_root.XamlRoot) as DependencyObject;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                Answer(_session.Cancel());
                e.Handled = true;
                break;
            // Enter opens, unless it is meant for another button that has the focus (Cancel, Copy).
            case VirtualKey.Enter when focused is not Button || focused == _open:
                Answer(_session.Confirm());
                e.Handled = true;
                break;
            case >= VirtualKey.Number1 and <= VirtualKey.Number9 when !control:
                Choose(e.Key - VirtualKey.Number0, e);
                break;
            case >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 when !control:
                Choose(e.Key - VirtualKey.NumberPad0, e);
                break;
            case VirtualKey.C when control && IsInList(focused):
                CopyUrl();
                e.Handled = true;
                break;
            // The list moves its own selection when it has the focus; from anywhere else, move it here.
            case VirtualKey.Up or VirtualKey.Down when !IsInList(focused):
                _session.MoveSelection(e.Key == VirtualKey.Up ? -1 : 1);
                _list.SelectedIndex = _session.SelectedIndex;
                (_list.ContainerFromIndex(_session.SelectedIndex) as ListViewItem)?.Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
        }
    }

    private void Choose(int number, KeyRoutedEventArgs e)
    {
        if (_session.ChooseNumber(number) is { } chosen)
        {
            Answer(chosen);
            e.Handled = true;
        }
    }

    private bool IsInList(DependencyObject? element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == _list)
            {
                return true;
            }
        }
        return false;
    }

    private void OnSelectionChanged()
    {
        if (_list.SelectedIndex >= 0)
        {
            _session.Select(_list.SelectedIndex);
            UpdateOpenButton();
        }
    }

    private void OnTick()
    {
        if (_session.Tick() is { } response)
        {
            Answer(response);
            return;
        }
        UpdateOpenButton();
    }

    private void UpdateOpenButton()
        => _open.Content = _session.AutoSelectRemaining is { } remaining
            ? L("Open ({0})", Math.Ceiling(remaining.TotalSeconds))
            : L("Open");

    private void CopyUrl()
    {
        WindowsUserNotifier.CopyToClipboard(_session.Request.Url);
        _copyIcon.Glyph = Glyphs.CheckMark;
        var restore = _window.DispatcherQueue.CreateTimer();
        restore.Interval = TimeSpan.FromSeconds(1.5);
        restore.IsRepeating = false;
        restore.Tick += (_, _) => _copyIcon.Glyph = Glyphs.Copy;
        restore.Start();
        _session.NoteActivity();
    }

    private void Answer(PickerResponse response)
    {
        if (_answered)
        {
            return;
        }
        _answered = true;
        _timer?.Stop();
        // Answer first: the Router opens the browser while the window goes away.
        _answer(response);
        if (!_closing)
        {
            _window.Close();
        }
        _quit();
    }

    /// <summary>Developer aid: writes the window's content as PNG, for checking the layout without a screenshot.</summary>
    private async Task Snapshot(string path)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(_root);
        var pixels = await bitmap.GetPixelsAsync();
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var scale = _root.XamlRoot.RasterizationScale;
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96 * scale, 96 * scale, pixels.ToArray());
        await encoder.FlushAsync();
        using var file = File.Create(path);
        await stream.AsStreamForRead().CopyToAsync(file);
    }

    // Like the AppKit picker: the host large, the whole URL small below it, the copy button at the end. Dragging the
    // header moves the window, which has no title bar.
    private FrameworkElement BuildHeader(string url)
    {
        var parts = PickerDisplay.SplitUrl(url);
        var icon = new FontIcon { Glyph = parts.Host.Length > 0 ? Glyphs.Link : Glyphs.Page, FontSize = 22, Foreground = Brush("TextFillColorSecondaryBrush") };

        var host = new TextBlock
        {
            Text = parts.Host.Length > 0 ? parts.Host : Path.GetFileName(parts.AfterHost.TrimEnd('/')),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var full = new TextBlock
        {
            Text = url,
            FontSize = 12,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(full, url);
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { host, full } };

        var copy = new Button
        {
            Content = _copyIcon,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8),
        };
        ToolTipService.SetToolTip(copy, L("Copy URL"));
        copy.Click += (_, _) => CopyUrl();

        var header = Columns(12, (icon, GridLength.Auto), (texts, new GridLength(1, GridUnitType.Star)), (copy, GridLength.Auto));
        header.Background = new SolidColorBrush(Colors.Transparent);
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
            {
                Win32.BeginDrag(Handle);
            }
        };
        return header;
    }

    // Part of the content rather than the footer, which holds only the dialog's buttons.
    private CheckBox? BuildRemember(PickerRequest request)
    {
        if (!_session.CanRemember)
        {
            return null;
        }
        var remember = new CheckBox
        {
            Content = new TextBlock { Text = L("Always open {0} with this choice", PickerDisplay.SplitUrl(request.Url).Host), TextTrimming = TextTrimming.CharacterEllipsis },
            IsChecked = _session.Remember,
        };
        remember.Checked += (_, _) => _session.Remember = true;
        remember.Unchecked += (_, _) => _session.Remember = false;
        return remember;
    }

    private ListViewItem BuildRow(PickerCandidate candidate, int index)
    {
        var name = new TextBlock { Text = candidate.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name } };
        if (PickerDisplay.DetailLine(candidate) is { Length: > 0 } detail)
        {
            texts.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = Brush("TextFillColorSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var columns = new List<(UIElement, GridLength)>
        {
            (BuildIcon(candidate, index), GridLength.Auto),
            (texts, new GridLength(1, GridUnitType.Star)),
        };
        if (index < 9)
        {
            columns.Add((Keycap((index + 1).ToString(CultureInfo.InvariantCulture)), GridLength.Auto));
        }

        var item = new ListViewItem
        {
            Content = Columns(12, [.. columns]),
            MinHeight = RowHeight,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 0, 12, 0),
        };
        AutomationProperties(item, candidate);
        item.DoubleTapped += (_, _) =>
        {
            _session.Select(index);
            Answer(_session.Confirm());
        };
        return item;
    }

    private static void AutomationProperties(ListViewItem item, PickerCandidate candidate)
        => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, PickerDisplay.DetailLine(candidate) is { Length: > 0 } detail ? $"{candidate.Name}, {detail}" : candidate.Name);

    // The browser's icon with the browser profile's picture as a small badge in the corner, like the browsers' own profile menus.
    private FrameworkElement BuildIcon(PickerCandidate candidate, int index)
    {
        const double Size = 36;
        var canvas = new Grid { Width = Size, Height = Size };
        if (_images.Icon(index) is { } image)
        {
            canvas.Children.Add(new Image { Source = image, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        }
        else
        {
            // A command, or a browser that is not installed here.
            canvas.Children.Add(new FontIcon { Glyph = candidate.Browser is null ? Glyphs.Command : Glyphs.Globe, FontSize = 24, Foreground = Brush("TextFillColorSecondaryBrush") });
        }

        if (_images.Avatar(index) is { } picture)
        {
            canvas.Children.Add(new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = new ImageBrush { ImageSource = picture, Stretch = Stretch.UniformToFill },
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            });
        }
        return canvas;
    }

    // The 1-9 shortcut, drawn like a key cap.
    private static Border Keycap(string text) => new()
    {
        Width = 22,
        Height = 22,
        CornerRadius = new CornerRadius(5),
        BorderThickness = new Thickness(1),
        BorderBrush = Brush("TextFillColorTertiaryBrush"),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static Grid Columns(double spacing, params (UIElement Element, GridLength Width)[] columns)
    {
        var grid = new Grid { ColumnSpacing = spacing };
        for (var i = 0; i < columns.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = columns[i].Width });
            Grid.SetColumn((FrameworkElement)columns[i].Element, i);
            grid.Children.Add(columns[i].Element);
        }
        return grid;
    }

    private static Brush Brush(string key) => Resource<Brush>(key);

    // NativeAOT: CsWinRT wraps what WinUI hands back as the most specific projected class whose metadata survived
    // trimming, which are the classes the code constructs. Nothing constructs a Style, so the accent button style came
    // back as a DependencyObject and the cast failed.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Style))]
    private static T Resource<T>(string key)
        where T : class
    {
        var value = Application.Current.Resources[key];
        return value as T ?? throw new InvalidCastException($"The resource {key} is a {value?.GetType().Name}, not a {typeof(T).Name}.");
    }

    /// <summary>Segoe Fluent Icons.</summary>
    private static class Glyphs
    {
        public const string Link = "\uE71B";
        public const string Page = "\uE8A5";
        public const string Copy = "\uE8C8";
        public const string CheckMark = "\uE73E";
        public const string Command = "\uE756";
        public const string Globe = "\uE774";
    }
}
