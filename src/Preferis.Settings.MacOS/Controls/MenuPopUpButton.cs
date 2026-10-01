using AppKit;
using CoreGraphics;
using ObjCRuntime;

namespace Preferis.Settings.MacOS.Controls;

/// <summary>
/// A pop-up (or pull-down) button as System Settings draws one in a form row: the value, then a round badge with the
/// chevron. No AppKit bezel looks like this (SwiftUI draws its own), so the button is borderless without its arrow, and
/// the badge is a subview that lets clicks through to the button.
/// </summary>
internal sealed class MenuPopUpButton : NSPopUpButton
{
    private const double BadgeSize = 20;
    private const double Gap = 8;
    private const double Height = 24;
    private readonly NSView? _badge;

    public MenuPopUpButton(bool pullsDown)
        : base(CGRect.Empty, pullsDown)
    {
        Bordered = false;
        ((NSPopUpButtonCell)Cell).ArrowPosition = NSPopUpArrowPosition.None;
        Alignment = NSTextAlignment.Left;
        _badge = new Badge(pullsDown ? "chevron.down" : "chevron.up.chevron.down");
        AddSubview(_badge);
    }

    public MenuPopUpButton(NativeHandle handle)
        : base(handle)
    {
    }

    public override CGSize IntrinsicContentSize
    {
        get
        {
            var size = base.IntrinsicContentSize;
            // As tall as a push button, like SwiftUI's pop-up buttons in a form.
            return new CGSize(size.Width + Gap + BadgeSize, Math.Max((double)size.Height, Height));
        }
    }

    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            base.Enabled = value;
            if (_badge is not null)
            {
                _badge.AlphaValue = value ? 1 : 0.4f;
            }
        }
    }

    public override void Layout()
    {
        base.Layout();
        if (_badge is not null)
        {
            _badge.Frame = new CGRect(Bounds.Width - (nfloat)BadgeSize, (Bounds.Height - (nfloat)BadgeSize) / 2, (nfloat)BadgeSize, (nfloat)BadgeSize);
        }
    }

    private sealed class Badge : NSBox
    {
        public Badge(string symbol)
        {
            BoxType = NSBoxType.NSBoxCustom;
            BorderWidth = 0;
            CornerRadius = (nfloat)(BadgeSize / 2);
            // The badge of System Settings' pop-up buttons, measured: secondarySystemFill over the group.
            FillColor = OperatingSystem.IsMacOSVersionAtLeast(14) ? NSColor.SecondarySystemFill : NSColor.Label.ColorWithAlphaComponent(0.06f);
            ContentViewMargins = CGSize.Empty;
            var image = NSImageView.FromImage(Images.Symbol(symbol, 9, NSFontWeight.Bold)!);
            image.ContentTintColor = NSColor.Label;
            ContentView = image;
        }

        public Badge(NativeHandle handle)
            : base(handle)
        {
        }

        // Clicks on the badge open the button's menu.
        public override NSView? HitTest(CGPoint point) => null;
    }
}
