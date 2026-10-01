using Preferis.Platform.MacOS;

namespace Preferis.Platform.Tests.MacOS;

// Talks to the real NSWorkspace; Safari ships with every macOS, so it is a stable subject.
public class MacApplicationIconsTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void An_application_icon_is_returned_as_png()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var png = MacApplicationIcons.GetPng("/Applications/Safari.app", 64);

        Assert.NotNull(png);
        Assert.Equal(PngSignature, png.Take(8));
        // Large enough to be a real picture, small enough to show that a 64 pt representation was chosen.
        Assert.InRange(png.Length, 1_000, 400_000);
    }

    [Fact]
    public void A_missing_path_has_no_icon()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        Assert.Null(MacApplicationIcons.GetPng("/Applications/Does Not Exist.app", 64));
    }
}
