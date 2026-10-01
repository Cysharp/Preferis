using System.ComponentModel;
using AppKit;
using CoreGraphics;
using Preferis.Core.Configuration;

namespace Preferis.Settings.MacOS.Controls;

/// <summary>Pieces shared by the profile and rule editors.</summary>
internal static class EditorParts
{
    /// <summary>
    /// The first group of an editor, like the top of a System Settings pane: an icon, a title and a subtitle, followed
    /// by the problems of the item when it has any.
    /// </summary>
    public static NSBox Header(NSView icon, NSTextField title, NSTextField subtitle, INotifyPropertyChanged source, string issuesProperty, Func<IReadOnlyList<ConfigIssue>> issues, Bindings bindings)
    {
        title.Font = NSFont.SystemFontOfSize(15, NSFontWeight.Semibold)!;
        var texts = Ui.VStack(2, title, subtitle);
        Ui.FillWidth(subtitle, texts);
        texts.SetHuggingPriority(1, NSLayoutConstraintOrientation.Horizontal);
        var identity = Ui.HStack(10, icon, texts);
        identity.Distribution = NSStackViewDistribution.Fill;
        identity.EdgeInsets = new NSEdgeInsets(10, (nfloat)Form.Inset, 10, (nfloat)Form.Inset);

        var problems = Ui.VStack(4);
        var problemsRow = Form.Wide(problems);
        var group = Form.Group(identity, problemsRow);
        bindings.Observe(source, () =>
        {
            foreach (var view in problems.ArrangedSubviews)
            {
                view.RemoveFromSuperview();
            }
            foreach (var issue in issues())
            {
                var message = Ui.Flowing(issue.Message, 12, issue.Severity == IssueSeverity.Error ? NSColor.SystemRed : NSColor.SystemOrange);
                problems.AddArrangedSubview(message);
                Ui.FillWidth(message, problems);
            }
            problemsRow.Hidden = issues().Count == 0;
            Form.UpdateSeparators(group);
        }, issuesProperty);
        return group;
    }

    /// <summary>A 32 pt icon for <see cref="Header"/>.</summary>
    public static NSImageView Icon()
    {
        var icon = new NSImageView { ImageScaling = NSImageScale.ProportionallyUpOrDown, ContentTintColor = NSColor.SecondaryLabel };
        icon.WidthAnchor.ConstraintEqualTo(32).Active = true;
        icon.HeightAnchor.ConstraintEqualTo(32).Active = true;
        return icon;
    }

    /// <summary>What an editor shows when nothing is selected.</summary>
    public static NSView Empty(string title, string message)
    {
        var text = Ui.VStack(6,
            Ui.Label(title, 17, NSFontWeight.Semibold, NSColor.SecondaryLabel),
            Ui.Wrapping(message, 320, 12));
        text.Alignment = NSLayoutAttribute.CenterX;
        var host = new NSView();
        host.AddSubview(text);
        text.TranslatesAutoresizingMaskIntoConstraints = false;
        text.CenterXAnchor.ConstraintEqualTo(host.CenterXAnchor).Active = true;
        text.CenterYAnchor.ConstraintEqualTo(host.CenterYAnchor).Active = true;
        return host;
    }

    /// <summary>A white symbol on a colored rounded square, like the icons of System Settings.</summary>
    public static NSView SymbolBadge(string symbol, NSColor color, double size)
    {
        var badge = new NSBox
        {
            BoxType = NSBoxType.NSBoxCustom,
            BorderWidth = 0,
            CornerRadius = (nfloat)(size * 0.27),
            FillColor = color,
            ContentViewMargins = CGSize.Empty,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        var image = NSImageView.FromImage(Images.Symbol(symbol, size * 0.55, NSFontWeight.Semibold)!);
        image.ContentTintColor = NSColor.White;
        badge.ContentView = image;
        badge.WidthAnchor.ConstraintEqualTo((nfloat)size).Active = true;
        badge.HeightAnchor.ConstraintEqualTo((nfloat)size).Active = true;
        return badge;
    }
}
