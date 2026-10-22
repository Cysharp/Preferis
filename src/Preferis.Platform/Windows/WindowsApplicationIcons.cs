using System.Runtime.Versioning;

namespace Preferis.Platform.Windows;

/// <summary>
/// Program icons as PNG, for the settings view models (shared with macOS, so they cannot hold a platform image) and the
/// picker; and Preferis's own icon for its windows. Pure Win32: no System.Drawing, which NativeAOT trims poorly.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe class WindowsApplicationIcons
{
    // The compiler writes ApplicationIcon as icon group 32512 (the number of IDI_APPLICATION); NativeAOT copies the
    // Win32 resources of the assembly over to the executable.
    private const int ApplicationIconId = 32512;

    /// <summary>
    /// The icon this program was built with (<c>ApplicationIcon</c>), for its windows: WinUI gives a window no icon, and
    /// the taskbar thumbnail and Alt+Tab show the window's icon rather than the executable's (docs/Platform.md §2.7).
    /// </summary>
    /// <param name="pixelSize">Edge length; the closest image of the icon is picked, and scaled when none matches.</param>
    /// <returns>An icon handle the caller owns (DestroyIcon), or zero when the program has no icon.</returns>
    public static IntPtr LoadApplicationIcon(int pixelSize) => LoadApplicationIcon(Native.GetModuleHandle(null), pixelSize);

    /// <param name="module">The executable or library whose icon to load, loaded or mapped as a data file.</param>
    internal static IntPtr LoadApplicationIcon(IntPtr module, int pixelSize)
        => Native.LoadImage(module, ApplicationIconId, Native.ImageIcon, pixelSize, pixelSize, 0);

    /// <param name="path">An executable (or any file with an icon).</param>
    /// <param name="pixelSize">Edge length of the bitmap; ask for the displayed size times the display scale.</param>
    /// <returns>The PNG bytes, or null when there is no such file or no icon could be extracted.</returns>
    public static byte[]? GetPng(string path, int pixelSize)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // The size picks (or scales to) the matching image of the icon resource.
        if (Native.SHDefExtractIcon(path, 0, 0, out var icon, out var small, (uint)pixelSize) != 0 || icon == IntPtr.Zero)
        {
            return null;
        }
        if (small != IntPtr.Zero)
        {
            Native.DestroyIcon(small);
        }

        try
        {
            if (!Native.GetIconInfo(icon, out var info))
            {
                return null;
            }
            try
            {
                return info.Color == IntPtr.Zero ? null : ToPng(info);
            }
            finally
            {
                Native.DeleteObject(info.Color);
                Native.DeleteObject(info.Mask);
            }
        }
        finally
        {
            Native.DestroyIcon(icon);
        }
    }

    private static byte[]? ToPng(Native.IconInfo info)
    {
        Native.Bitmap bitmap;
        if (Native.GetObjectW(info.Color, sizeof(Native.Bitmap), &bitmap) == 0)
        {
            return null;
        }
        var width = bitmap.Width;
        var height = bitmap.Height;
        var color = ReadPixels(info.Color, width, height);
        if (color is null)
        {
            return null;
        }

        // Old icons have no alpha channel; their transparency is in the AND mask (white = transparent).
        var hasAlpha = false;
        for (var i = 3; i < color.Length; i += 4)
        {
            hasAlpha |= color[i] != 0;
        }
        if (!hasAlpha && info.Mask != IntPtr.Zero && ReadPixels(info.Mask, width, height) is { } mask)
        {
            for (var i = 0; i < color.Length; i += 4)
            {
                color[i + 3] = mask[i] == 0 ? (byte)255 : (byte)0;
            }
        }
        return PngEncoder.Encode(width, height, color);
    }

    // 32 bits per pixel, top-down, whatever the bitmap's own format.
    private static byte[]? ReadPixels(IntPtr bitmap, int width, int height)
    {
        var header = new Native.BitmapInfoHeader
        {
            Size = (uint)sizeof(Native.BitmapInfoHeader),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        // GetDIBits writes a color table after the header for some formats; leave room for it.
        var info = stackalloc byte[sizeof(Native.BitmapInfoHeader) + 256 * 4];
        *(Native.BitmapInfoHeader*)info = header;
        var dc = Native.GetDC(IntPtr.Zero);
        try
        {
            fixed (byte* bits = pixels)
            {
                return Native.GetDIBits(dc, bitmap, 0, (uint)height, bits, (Native.BitmapInfoHeader*)info, Native.DibRgbColors) == height ? pixels : null;
            }
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, dc);
        }
    }
}
