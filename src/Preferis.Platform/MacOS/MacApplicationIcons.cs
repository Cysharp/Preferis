using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Preferis.Platform.MacOS;

/// <summary>
/// Application icons as PNG for UIs that cannot use an NSImage directly (the Avalonia settings UI).
/// The picker, being AppKit, asks NSWorkspace itself.
/// </summary>
[SupportedOSPlatform("macos")]
public static unsafe class MacApplicationIcons
{
    private const nuint NSBitmapImageFileTypePNG = 4;

    /// <param name="path">An application bundle (or any file).</param>
    /// <param name="pixelSize">Edge length of the bitmap; ask for twice the displayed size on Retina screens.</param>
    /// <returns>The PNG bytes, or null when there is no such file or the icon could not be rendered.</returns>
    public static byte[]? GetPng(string path, int pixelSize)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return null;
        }

        using var pool = ObjC.AutoreleasePool();
        var image = ObjC.Send(ObjC.SharedWorkspace, "iconForFile:", ObjC.NSString(path));
        if (image == IntPtr.Zero)
        {
            return null;
        }

        // Icons hold several representations; proposing a rect makes AppKit pick (or render) the one for that size.
        var rect = new Rect(0, 0, pixelSize, pixelSize);
        var cgImage = ObjC.Send(image, "CGImageForProposedRect:context:hints:", (IntPtr)(&rect), IntPtr.Zero, IntPtr.Zero);
        if (cgImage == IntPtr.Zero)
        {
            return null;
        }

        var bitmap = ObjC.Send(ObjC.Send(ObjC.GetClass("NSBitmapImageRep"), "alloc"), "initWithCGImage:", cgImage);
        try
        {
            var data = ObjC.Send(bitmap, "representationUsingType:properties:", NSBitmapImageFileTypePNG, ObjC.Send(ObjC.GetClass("NSDictionary"), "dictionary"));
            if (data == IntPtr.Zero)
            {
                return null;
            }
            var bytes = new byte[(int)ObjC.SendNUInt(data, "length")];
            Marshal.Copy(ObjC.Send(data, "bytes"), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            ObjC.SendVoid(bitmap, "release");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Rect(double x, double y, double width, double height)
    {
        public readonly double X = x;
        public readonly double Y = y;
        public readonly double Width = width;
        public readonly double Height = height;
    }
}
