using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Preferis.Core.Configuration;

namespace Preferis.Settings.Windows.Controls;

/// <summary>Pieces shared by the profile and rule editors.</summary>
internal static class EditorParts
{
    /// <summary>
    /// The first card of an editor, like the top of a page in the Settings app: an icon, a title and a subtitle, followed
    /// by the problems of the item when it has any.
    /// </summary>
    public static Border Header(UIElement icon, TextBlock title, TextBlock subtitle, INotifyPropertyChanged source, string issuesProperty, Func<IReadOnlyList<ConfigIssue>> issues, Bindings bindings)
    {
        title.FontSize = 20;
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        subtitle.TextWrapping = TextWrapping.Wrap;
        var identity = Ui.Columns(16, (icon, Ui.Auto), (Ui.VStack(2, title, subtitle), Ui.Star));

        var problems = new StackPanel { Spacing = 4 };
        bindings.Observe(source, () =>
        {
            problems.Children.Clear();
            foreach (var issue in issues())
            {
                var error = issue.Severity == IssueSeverity.Error;
                var color = Ui.Brush(error ? "SystemFillColorCriticalBrush" : "SystemFillColorCautionBrush");
                var text = Ui.Wrapping(issue.Message, 12, Ui.Brush("TextFillColorPrimaryBrush"));
                problems.Children.Add(Ui.Columns(8, (Ui.Glyph(error ? Ui.Glyphs.Error : Ui.Glyphs.Warning, 14, color), Ui.Auto), (text, Ui.Star)));
            }
            Ui.Show(problems, problems.Children.Count > 0);
        }, issuesProperty);

        var box = Form.Box(Ui.VStack(12, identity, problems));
        box.Padding = new Thickness(16);
        return box;
    }

    /// <summary>A 32 px image for <see cref="Header"/> that the editor updates.</summary>
    public static Grid IconHost() => new() { Width = 32, Height = 32 };

    public static void SetIcon(Grid host, byte[]? png, string fallbackGlyph)
    {
        host.Children.Clear();
        host.Children.Add(Ui.Image(png, 32) ?? (UIElement)Ui.Glyph(fallbackGlyph, 26, Ui.Brush("TextFillColorSecondaryBrush")));
    }
}
