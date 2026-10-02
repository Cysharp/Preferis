using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Preferis.Settings.Windows.Controls;

/// <summary>
/// Pages laid out like the Windows 11 Settings app: sections of cards, each card a setting with its name and description
/// on the left and its control on the right, 4 px apart. The sizes are those of the Settings app (and of the Community
/// Toolkit's SettingsCard, which is not used here: a Border and a Grid are enough, and it is one XAML library fewer to
/// prove under NativeAOT).
/// </summary>
internal static class Form
{
    public const double CardSpacing = 4;
    private const double PageMaxWidth = 1000;

    /// <summary>A scrolling page of sections.</summary>
    public static ScrollViewer Page(params UIElement[] sections)
    {
        var stack = new StackPanel { MaxWidth = PageMaxWidth, Padding = new Thickness(0, 0, 0, 32) };
        foreach (var section in sections)
        {
            stack.Children.Add(section);
        }
        return new ScrollViewer
        {
            Content = stack,
            Padding = new Thickness(32, 0, 32, 0),
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    /// <summary>A titled group of cards, with an explanation under them.</summary>
    public static StackPanel Section(string? title, string? footer, params UIElement[] cards)
    {
        var section = new StackPanel { Spacing = CardSpacing };
        if (title is not null)
        {
            section.Children.Add(Header(title));
        }
        foreach (var card in cards)
        {
            section.Children.Add(card);
        }
        if (footer is not null)
        {
            section.Children.Add(Footer(footer));
        }
        return section;
    }

    public static StackPanel Section(string? title, params UIElement[] cards) => Section(title, null, cards);

    /// <summary>A section title, with something (a button) at its end.</summary>
    public static FrameworkElement Header(string title, UIElement? accessory = null)
    {
        var text = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var header = accessory is null ? (FrameworkElement)text : Ui.Columns(8, (text, Ui.Star), (accessory, Ui.Auto));
        header.Margin = new Thickness(1, 28, 0, 4);
        return header;
    }

    public static TextBlock Footer(string text)
    {
        var footer = Ui.Wrapping(text);
        footer.Margin = new Thickness(1, 4, 0, 0);
        return footer;
    }

    /// <summary>A setting: name and description on the left, its control on the right.</summary>
    public static Border Card(string header, UIElement? control, string? description = null, UIElement? icon = null)
        => Card(Ui.Label(header), control, description is null ? null : Ui.Wrapping(description), icon);

    public static Border Card(UIElement header, UIElement? control, TextBlock? description = null, UIElement? icon = null)
    {
        if (header is TextBlock title)
        {
            // Next to a wide control the title wraps rather than losing its end.
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
        }
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, MinWidth = 120 };
        texts.Children.Add(header);
        if (description is not null)
        {
            texts.Children.Add(description);
        }

        var columns = new List<(UIElement, GridLength)>();
        if (icon is not null)
        {
            columns.Add((icon, Ui.Auto));
        }
        columns.Add((texts, Ui.Star));
        if (control is not null)
        {
            if (control is FrameworkElement element)
            {
                element.VerticalAlignment = VerticalAlignment.Center;
            }
            columns.Add((control, Ui.Auto));
        }
        return Box(Ui.Columns(16, [.. columns]));
    }

    /// <summary>A card around content that takes the whole width (an editor, a list).</summary>
    public static Border Wide(UIElement content) => Box(content);

    /// <summary>The look of a card: the card fill, a hairline border, small round corners.</summary>
    public static Border Box(UIElement content) => new()
    {
        Child = content,
        Background = Ui.Brush("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Ui.Brush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(16, 12, 16, 12),
        MinHeight = 64,
    };

    /// <summary>What an editor shows when nothing is selected.</summary>
    public static FrameworkElement Empty(string title, string message)
    {
        var text = Ui.VStack(6, Ui.Label(title, 20, semibold: true, color: Ui.Brush("TextFillColorSecondaryBrush")), Ui.Wrapping(message));
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.MaxWidth = 360;
        return text;
    }
}
