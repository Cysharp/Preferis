using System.Buffers.Binary;
using System.IO.Compression;

namespace Preferis.Platform.Windows;

/// <summary>
/// Writes 32-bit pixels as PNG. Icons are tiny, so the simplest valid encoding (no row filters) is enough, and it avoids a
/// dependency on System.Drawing or WIC for one format.
/// </summary>
internal static class PngEncoder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <param name="bgra">Top-down rows of <paramref name="width"/> pixels, 4 bytes each in GDI's blue, green, red, alpha order.</param>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> bgra)
    {
        var raw = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 4);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var source = (y * width + x) * 4;
                var target = row + 1 + x * 4;
                raw[target] = bgra[source + 2];
                raw[target + 1] = bgra[source + 1];
                raw[target + 2] = bgra[source];
                raw[target + 3] = bgra[source + 3];
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 6; // RGBA

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);

        Span<byte> typeBytes = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            typeBytes[i] = (byte)type[i];
        }
        stream.Write(typeBytes);
        stream.Write(data);

        // The CRC covers the type and the data.
        var crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
