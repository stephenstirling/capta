using System.IO.Compression;
using System.Text;

namespace Capta.Capture;

/// <summary>Edits the chunks of an encoded PNG, for what WinRT's PNG encoder can't write itself.</summary>
public static class PngChunks
{
    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] s_crcTable = BuildCrcTable();

    /// <summary>
    /// Embeds an ICC profile as an iCCP chunk straight after IHDR, dropping the chunks it
    /// supersedes (sRGB, gAMA, cHRM and any earlier iCCP).
    /// </summary>
    /// <param name="name">Profile name, 1 to 79 Latin-1 characters.</param>
    public static byte[] WithIccProfile(byte[] png, string name, byte[] profile)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new ArgumentException("Not a PNG.", nameof(png));

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(profile);
        using var iccp = new MemoryStream();
        iccp.Write(Encoding.Latin1.GetBytes(name));
        iccp.WriteByte(0); // name terminator
        iccp.WriteByte(0); // compression method: zlib
        compressed.Position = 0;
        compressed.CopyTo(iccp);

        var output = new MemoryStream(png.Length + (int)iccp.Length + 12);
        output.Write(Signature);
        var offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            var length = (int)ReadUInt32(png, offset);
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var total = 12 + length;
            if (length < 0 || offset + total > png.Length)
                throw new InvalidDataException("Truncated PNG chunk.");

            if (type is not ("sRGB" or "gAMA" or "cHRM" or "iCCP"))
                output.Write(png, offset, total);
            if (type == "IHDR")
                WriteChunk(output, "iCCP", iccp.ToArray());
            offset += total;
            if (type == "IEND") break;
        }
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        WriteUInt32(chunk, 0, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, 0, 4, chunk, 4);
        data.CopyTo(chunk, 8);
        WriteUInt32(chunk, 8 + data.Length, Crc32(chunk.AsSpan(4, 4 + data.Length)));
        output.Write(chunk);
    }

    /// <summary>The PNG chunk CRC (ISO 3309 / zlib CRC-32) over the type and data.</summary>
    internal static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
