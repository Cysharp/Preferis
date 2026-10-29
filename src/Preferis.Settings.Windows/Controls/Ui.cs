using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Color = Windows.UI.Color;

namespace Preferis.Settings.Windows.Controls;

/// <summary>Small factories for the controls the pages are built from, styled like the Windows 11 Settings app.</summary>
internal static class Ui
{
    public static readonly FontFamily Monospace = new("Cascadia Mono, Consolas");

    public static TextBlock Label(string text, double size = 14, bool semibold = false, Brush? color = null)
    {
        var label = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (semibold)
        {
            label.FontWeight = FontWeights.SemiBold;
        }
        if (color is not null)
        {
            label.Foreground = color;
        }
        return label;
    }

    /// <summary>Second lines and descriptions: 12 px in the secondary text color.</summary>
    public static TextBlock Secondary(string text, double size = 12) => Label(text, size, color: Brush("TextFillColorSecondaryBrush"));

    /// <summary>Text that wraps instead of being cut off.</summary>
    public static TextBlock Wrapping(string text, double size = 12, Brush? color = null) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = color ?? Brush("TextFillColorSecondaryBrush"),
        IsTextSelectionEnabled = true,
    };

    public static TextBlock Code(string text) => new()
    {
        Text = text,
        FontFamily = Monospace,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };

    public static Button Button(string text, Action? click = null)
    {
        var button = new Button { Content = text };
        if (click is not null)
        {
            button.Click += (_, _) => click();
        }
        return button;
    }

    public static Button AccentButton(string text) => new() { Content = text, Style = Resource<Style>("AccentButtonStyle") };

    /// <summary>A borderless button showing a Segoe Fluent Icons glyph, with a tooltip.</summary>
    public static Button IconButton(string glyph, string tip, double size = 14)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = size },
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    public static TextBox TextBox(string placeholder = "", bool monospaced = false, double width = 280)
    {
        var box = new TextBox { PlaceholderText = placeholder, Width = width, HorizontalAlignment = HorizontalAlignment.Right };
        if (monospaced)
        {
            box.FontFamily = Monospace;
        }
        return box;
    }

    /// <summary>One value per line (arguments), Return starts a new one.</summary>
    public static TextBox LinesBox(string placeholder = "") => new()
    {
        PlaceholderText = placeholder,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = Monospace,
        Width = 280,
        MinHeight = 64,
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    /// <summary>A switch without the "On" / "Off" text, which the Settings app does not show either.</summary>
    public static ToggleSwitch Switch() => new() { OnContent = "", OffContent = "", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right };

    public static ComboBox ComboBox(double minWidth = 200) => new() { MinWidth = minWidth, HorizontalAlignment = HorizontalAlignment.Right };

    /// <summary>An entry of a drop-down or menu: the text, with an icon in front when there is one.</summary>
    public static UIElement MenuContent(string text, byte[]? iconPng)
    {
        if (Image(iconPng, 16) is not { } image)
        {
            return new TextBlock { Text = text };
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { image, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
    }

    public static Image? Image(byte[]? png, double size)
        => Images.FromPng(png) is { } source ? new Image { Source = source, Width = size, Height = size } : null;

    public static FontIcon Glyph(string glyph, double size = 16, Brush? color = null)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = size };
        if (color is not null)
        {
            icon.Foreground = color;
        }
        return icon;
    }

    /// <summary>A white glyph on a colored rounded square, like the page icons of the Settings app.</summary>
    public static Border Badge(string glyph, Color color, double size = 32) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.22),
        Background = new SolidColorBrush(color),
        Child = new FontIcon { Glyph = glyph, FontSize = size * 0.5, Foreground = new SolidColorBrush(Colors.White) },
    };

    public static StackPanel VStack(double spacing, params UIElement[] children)
    {
        var stack = new StackPanel { Spacing = spacing };
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }
        return stack;
    }

    public static StackPanel HStack(double spacing, params UIElement[] children)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children)
        {
            stack.Children.Add(child);
        }
        return stack;
    }

    /// <summary>A grid of one row: each element with its column width.</summary>
    public static Grid Columns(double spacing, params (UIElement Element, GridLength Width)[] columns)
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

    public static GridLength Star => new(1, GridUnitType.Star);

    public static GridLength Auto => GridLength.Auto;

    public static Brush Brush(string key) => Resource<Brush>(key);

    public static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    // NativeAOT: CsWinRT wraps what WinUI hands back as the most specific projected class whose metadata survived
    // trimming, which are the classes the code constructs. Nothing constructs a Style, so the accent button style came
    // back as a DependencyObject and the cast failed (seen in the picker).
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Style))]
    public static T Resource<T>(string key)
        where T : class
    {
        var value = Application.Current.Resources[key];
        return value as T ?? throw new InvalidCastException($"The resource {key} is a {value?.GetType().Name}, not a {typeof(T).Name}.");
    }

    /// <summary>Segoe Fluent Icons.</summary>
    public static class Glyphs
    {
        public const string People = "\uE716";
        public const string Branch = "\uE8AB";
        public const string Play = "\uE768";
        public const string Settings = "\uE713";
        public const string Add = "\uE710";
        public const string Remove = "\uE738";
        public const string More = "\uE712";
        public const string Up = "\uE70E";
        public const string Down = "\uE70D";
        public const string Delete = "\uE74D";
        public const string Globe = "\uE774";
        public const string Command = "\uE756";
        public const string Star = "\uE735";
        public const string Error = "\uEA39";
        public const string Warning = "\uE7BA";
        public const string Completed = "\uE930";
        public const string Folder = "\uE838";
        public const string Next = "\uE76C";
        public const string Shield = "\uE83D";
        public const string Sync = "\uE895";
    }
}

/// <summary>Images for the PNG bytes of the view models (browser icons, profile pictures).</summary>
internal static class Images
{
    private static readonly ConditionalWeakTable<byte[], BitmapImage> Cache = new();

    public static BitmapImage? FromPng(byte[]? png) => png is { Length: > 0 } bytes ? Cache.GetValue(bytes, Load) : null;

    private static BitmapImage Load(byte[] png)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.SetSource(stream.AsRandomAccessStream());
        return image;
    }
}
