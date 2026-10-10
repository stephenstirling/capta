using System.IO.Compression;
using System.Text;
using Capta.Capture;

namespace Capta.Tests;

public class PngChunksTests
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Theory]
    [InlineData("IEND", 0xAE426082u)]
    [InlineData("123456789", 0xCBF43926u)] // the standard CRC-32 check value
    public void Crc32MatchesTheStandard(string text, uint expected) =>
        Assert.Equal(expected, PngChunks.Crc32(Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void InsertsTheProfileAfterIhdrAndDropsWhatItReplaces()
    {
        var png = Png(
            ("IHDR", new byte[13]),
            ("sRGB", [0]),
            ("gAMA", [0, 0, 0xB1, 0x8F]),
            ("cHRM", new byte[32]),
            ("iCCP", Encoding.ASCII.GetBytes("old\0\0")),
            ("tEXt", Encoding.ASCII.GetBytes("Capta\0{}")),
            ("IDAT", [1, 2, 3]),
            ("IEND", []));
        var profile = Enumerable.Range(0, 600).Select(i => (byte)(i * 7)).ToArray();

        var chunks = Chunks(PngChunks.WithIccProfile(png, "Display P3", profile));

        Assert.Equal(["IHDR", "iCCP", "tEXt", "IDAT", "IEND"], chunks.Select(c => c.Type));
        Assert.All(chunks, c => Assert.True(c.CrcValid, $"{c.Type} CRC"));
        Assert.Equal(Encoding.ASCII.GetBytes("Capta\0{}"), chunks[2].Data);
        Assert.Equal(new byte[] { 1, 2, 3 }, chunks[3].Data);

        var iccp = chunks[1].Data;
        var nul = Array.IndexOf(iccp, (byte)0);
        Assert.Equal("Display P3", Encoding.Latin1.GetString(iccp, 0, nul));
        Assert.Equal(0, iccp[nul + 1]); // zlib
        using var zlib = new ZLibStream(new MemoryStream(iccp, nul + 2, iccp.Length - nul - 2), CompressionMode.Decompress);
        using var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        Assert.Equal(profile, inflated.ToArray());
    }

    [Fact]
    public void RejectsWhatIsNotAPng() =>
        Assert.Throws<ArgumentException>(() => PngChunks.WithIccProfile([1, 2, 3, 4, 5, 6, 7, 8, 9], "x", [1]));

    [Fact]
    public void RejectsATruncatedChunk()
    {
        var png = Png(("IHDR", new byte[13]), ("IEND", []));
        Assert.Throws<InvalidDataException>(() => PngChunks.WithIccProfile(png[..(Signature.Length + 20)], "x", [1]));
    }

    internal static byte[] Png(params (string Type, byte[] Data)[] chunks)
    {
        var output = new MemoryStream();
        output.Write(Signature);
        foreach (var (type, data) in chunks)
        {
            var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            output.Write(BigEndian((uint)data.Length));
            output.Write(typeAndData);
            output.Write(BigEndian(PngChunks.Crc32(typeAndData)));
        }
        return output.ToArray();
    }

    internal static List<(string Type, byte[] Data, bool CrcValid)> Chunks(byte[] png)
    {
        Assert.Equal(Signature, png[..Signature.Length]);
        var chunks = new List<(string, byte[], bool)>();
        for (var offset = Signature.Length; offset < png.Length;)
        {
            var length = (int)ReadBigEndian(png, offset);
            var typeAndData = png[(offset + 4)..(offset + 8 + length)];
            var crc = ReadBigEndian(png, offset + 8 + length);
            chunks.Add((Encoding.ASCII.GetString(typeAndData, 0, 4), typeAndData[4..], crc == PngChunks.Crc32(typeAndData)));
            offset += 12 + length;
        }
        return chunks;
    }

    private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static uint ReadBigEndian(byte[] bytes, int offset) =>
        (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
}
