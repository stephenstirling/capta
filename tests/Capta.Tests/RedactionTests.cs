using Capta.Capture;
using Capta.Services;
using Windows.Graphics;

namespace Capta.Tests;

public class RedactionTests
{
    /// <summary>A w × h image whose R channel alternates 0 / 255 like a chessboard, opaque (HDR half floats 4 / 0 if asked).</summary>
    private static CapturedImage Chessboard(int w, int h, bool withHdr = false, bool transparent = false)
    {
        var pixels = new byte[w * h * 4];
        var hdr = withHdr ? new ushort[w * h * 4] : null;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            var on = (x + y) % 2 == 0;
            // Premultiplied BGRA, as CapturedImage stores it: a transparent pixel is all zeros.
            pixels[i + 2] = (byte)(on && !transparent ? 255 : 0);
            pixels[i + 3] = (byte)(transparent ? 0 : 255);
            if (hdr is not null)
            {
                hdr[i] = BitConverter.HalfToUInt16Bits((Half)(on ? 4f : 0f));
                hdr[i + 3] = BitConverter.HalfToUInt16Bits((Half)(transparent ? 0f : 1f));
            }
        }
        return new CapturedImage(w, h, pixels) { HdrPixels = hdr, HasTransparency = transparent };
    }

    private const uint OpaqueBlack = 0xFF000000;

    private static float Hdr(CapturedImage image, int x, int y, int channel) =>
        (float)BitConverter.UInt16BitsToHalf(image.HdrPixels![(y * image.Width + x) * 4 + channel]);

    [Fact]
    public void FillCoversTheAreaCompletelyAndLeavesTheRestAlone()
    {
        var image = Chessboard(60, 40);
        var filled = Redactor.Fill(image, [new RectInt32(10, 6, 30, 16)]);

        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 60; x++)
        {
            var inside = x >= 10 && x < 40 && y >= 6 && y < 22;
            Assert.Equal(inside ? OpaqueBlack : image.GetPixel(x, y), filled.GetPixel(x, y));
        }
    }

    [Fact]
    public void FillLeavesNothingToReadBack()
    {
        // Every pixel in the area is identical, whatever was there (unlike a blur, which keeps traces).
        var filled = Redactor.Fill(Chessboard(40, 30), [new RectInt32(5, 5, 20, 10)]);
        var inside = new HashSet<uint>();
        for (var y = 5; y < 15; y++)
        for (var x = 5; x < 25; x++)
            inside.Add(filled.GetPixel(x, y));
        Assert.Equal([OpaqueBlack], inside);
    }

    [Fact]
    public void FillLeavesTheOriginalAlone()
    {
        var image = Chessboard(40, 30);
        var before = (byte[])image.Pixels.Clone();
        Redactor.Fill(image, [new RectInt32(5, 5, 20, 10)]);
        Assert.Equal(before, image.Pixels);
    }

    [Fact]
    public void FillCoversTheHdrOriginalToo()
    {
        var image = Chessboard(60, 40, withHdr: true);
        var filled = Redactor.Fill(image, [new RectInt32(10, 6, 30, 16)]);

        Assert.NotSame(image.HdrPixels, filled.HdrPixels);
        foreach (var channel in new[] { 0, 1, 2 })
            Assert.Equal(0f, Hdr(filled, 20, 12, channel));
        Assert.Equal(1f, Hdr(filled, 20, 12, 3));
        Assert.Equal(Hdr(image, 1, 0, 0), Hdr(filled, 1, 0, 0)); // outside the area
    }

    [Fact]
    public void FillMakesTransparentPixelsOpaque()
    {
        var filled = Redactor.Fill(Chessboard(20, 20, withHdr: true, transparent: true), [new RectInt32(2, 2, 6, 6)]);
        Assert.Equal(OpaqueBlack, filled.GetPixel(3, 3));
        Assert.Equal(1f, Hdr(filled, 3, 3, 3));
        Assert.Equal(0u, filled.GetPixel(15, 15)); // still transparent outside
        Assert.True(filled.HasTransparency);
    }

    [Fact]
    public void AreasPartlyOutsideTheImageAreClamped()
    {
        var image = Chessboard(20, 20);
        var filled = Redactor.Fill(image, [new RectInt32(-5, -5, 15, 15), new RectInt32(100, 100, 10, 10), new RectInt32(15, 15, 50, 50)]);
        Assert.Equal(image.Width * image.Height * 4, filled.Pixels.Length);
        Assert.Equal(OpaqueBlack, filled.GetPixel(0, 0));
        Assert.Equal(OpaqueBlack, filled.GetPixel(9, 9));
        Assert.Equal(OpaqueBlack, filled.GetPixel(19, 19));
        Assert.Equal(image.GetPixel(12, 3), filled.GetPixel(12, 3));
    }

    [Fact]
    public void RedactionFillsOnlyTheAreasThatAreOn()
    {
        var image = Chessboard(80, 40);
        var matches = new[]
        {
            new PersonalInfoMatch(PersonalInfoKind.Email, "a@b.org", new RectInt32(0, 0, 24, 16)),
            new PersonalInfoMatch(PersonalInfoKind.Phone, "555-123-4567", new RectInt32(40, 20, 24, 16)),
        };
        var redaction = new Redaction(image, matches);

        Assert.Equal(2, redaction.RedactedCount);
        Assert.Equal(OpaqueBlack, redaction.Render().GetPixel(1, 0));
        Assert.Equal(OpaqueBlack, redaction.Render().GetPixel(41, 20));

        redaction.Set(1, false);
        Assert.Equal(1, redaction.RedactedCount);
        Assert.False(redaction.IsRedacted(1));
        Assert.Equal(OpaqueBlack, redaction.Render().GetPixel(1, 0));
        Assert.Equal(image.GetPixel(41, 20), redaction.Render().GetPixel(41, 20));
    }

    [Fact]
    public void UndoGivesBackTheOriginalImage()
    {
        var image = Chessboard(40, 30);
        var redaction = new Redaction(image, [new PersonalInfoMatch(PersonalInfoKind.Email, "a@b.org", new RectInt32(0, 0, 20, 12))]);
        redaction.SetAll(false);
        Assert.Same(image, redaction.Render());
        redaction.SetAll(true);
        Assert.NotSame(image, redaction.Render());
    }
}
