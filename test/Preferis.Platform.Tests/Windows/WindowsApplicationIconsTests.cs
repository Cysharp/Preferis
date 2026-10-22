using System.Buffers.Binary;
using System.IO.Compression;
using Preferis.Platform.Windows;

namespace Preferis.Platform.Tests.Windows;

public class WindowsApplicationIconsTests
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Png_encoding_writes_rgba_rows_from_bgra_pixels()
    {
        // 2 x 1: opaque red, half transparent blue (BGRA in memory, as GDI hands them out).
        byte[] bgra = [0, 0, 255, 255, 255, 0, 0, 128];

        var png = PngEncoder.Encode(2, 1, bgra);

        Assert.Equal(Signature, png[..8]);
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var header = chunks[0].Data;
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
        Assert.Equal([8, 6, 0, 0, 0], header[8..]);
        // One row: filter type 0, then RGBA.
        Assert.Equal([0, 255, 0, 0, 255, 0, 0, 255, 128], Inflate(chunks[1].Data));
        // The CRC of an empty IEND chunk is a well-known constant.
        Assert.Equal(0xAE426082u, chunks[2].Crc);
    }

    [Fact]
    public void Icons_of_executables_come_as_png_of_the_requested_size()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        var png = WindowsApplicationIcons.GetPng(explorer, 48);

        Assert.NotNull(png);
        Assert.Equal(Signature, png[..8]);
        var header = Chunks(png)[0].Data;
        Assert.Equal(48, BinaryPrimitives.ReadInt32BigEndian(header));
        Assert.Equal(48, BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)));
    }

    [Fact]
    public void Missing_files_have_no_icon()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Null(WindowsApplicationIcons.GetPng(@"C:\does\not\exist.exe", 32));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(48)]
    public void The_application_icon_is_loaded_at_the_requested_size(int size)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        // This test assembly embeds Preferis.ico with ApplicationIcon, as the programs do.
        var module = LoadAsDataFile(typeof(WindowsApplicationIconsTests).Assembly.Location);
        try
        {
            var icon = WindowsApplicationIcons.LoadApplicationIcon(module, size);

            Assert.NotEqual(IntPtr.Zero, icon);
            try
            {
                Assert.Equal((size, size), IconSize(icon));
            }
            finally
            {
                Native.DestroyIcon(icon);
            }
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    [Fact]
    public void Modules_built_without_an_application_icon_have_none()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var module = LoadAsDataFile(typeof(WindowsApplicationIcons).Assembly.Location);
        try
        {
            Assert.Equal(IntPtr.Zero, WindowsApplicationIcons.LoadApplicationIcon(module, 32));
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static IntPtr LoadAsDataFile(string path)
    {
        const uint LoadLibraryAsDatafile = 0x2;
        const uint LoadLibraryAsImageResource = 0x20;
        var module = LoadLibraryExW(path, IntPtr.Zero, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        Assert.NotEqual(IntPtr.Zero, module);
        return module;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (int Width, int Height) IconSize(IntPtr icon)
    {
        Assert.True(Native.GetIconInfo(icon, out var info));
        try
        {
            Assert.NotEqual(0, GetObjectW(info.Color, System.Runtime.InteropServices.Marshal.SizeOf<Native.Bitmap>(), out var bitmap));
            return (bitmap.Width, bitmap.Height);
        }
        finally
        {
            Native.DeleteObject(info.Color);
            Native.DeleteObject(info.Mask);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern int GetObjectW(IntPtr handle, int size, out Native.Bitmap bitmap);

    private static List<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[], uint)>();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png[(offset + 8)..(offset + 8 + length)];
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            chunks.Add((type, data, crc));
            offset += 12 + length;
        }
        return chunks;
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }
}
