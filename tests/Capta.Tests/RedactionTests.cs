using Capta.Capture;
using Capta.Services;
using Windows.Graphics;

namespace Capta.Tests;

public class RedactionTests
{
    /// <summary>A w × h image whose R channel alternates 0 / 255 like a chessboard (the worst case for a blur).</summary>
    private static CapturedImage Chessboard(int w, int h, bool withHdr = false)
    {
        var pixels = new byte[w * h * 4];
        var hdr = withHdr ? new ushort[w * h * 4] : null;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 4;
            var on = (x + y) % 2 == 0;
            pixels[i + 2] = (byte)(on ? 255 : 0);
            pixels[i + 3] = 255;
            if (hdr is not null)
            {
                hdr[i] = BitConverter.HalfToUInt16Bits((Half)(on ? 4f : 0f));
                hdr[i + 3] = BitConverter.HalfToUInt16Bits((Half)1f);
            }
        }
        return new CapturedImage(w, h, pixels) { HdrPixels = hdr };
    }

    private static float Hdr(CapturedImage image, int x, int y) =>
        (float)BitConverter.UInt16BitsToHalf(image.HdrPixels![(y * image.Width + x) * 4]);

    [Fact]
    public void BlurFlattensTheAreaAndLeavesTheRestAlone()
    {
        var image = Chessboard(60, 40);
        var area = new RectInt32(10, 6, 30, 16);
        var blurred = Redactor.Blur(image, [area]);

        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 60; x++)
        {
            var inside = x >= 10 && x < 40 && y >= 6 && y < 22;
            if (inside)
                Assert.InRange(blurred.Pixels[(y * 60 + x) * 4 + 2], 110, 145); // chessboard average, not 0 or 255
            else
                Assert.Equal(image.GetPixel(x, y), blurred.GetPixel(x, y));
        }
    }

    [Fact]
    public void BlurLeavesTheOriginalAlone()
    {
        var image = Chessboard(40, 30);
        var before = (byte[])image.Pixels.Clone();
        Redactor.Blur(image, [new RectInt32(5, 5, 20, 10)]);
        Assert.Equal(before, image.Pixels);
    }

    [Fact]
    public void BlurAlsoBlursTheHdrOriginal()
    {
        var image = Chessboard(60, 40, withHdr: true);
        var blurred = Redactor.Blur(image, [new RectInt32(10, 6, 30, 16)]);

        Assert.NotSame(image.HdrPixels, blurred.HdrPixels);
        Assert.InRange(Hdr(blurred, 20, 12), 1.5f, 2.5f); // between 0 and 4
        Assert.Equal(Hdr(image, 1, 0), Hdr(blurred, 1, 0)); // outside the area
    }

    [Fact]
    public void AreasPartlyOutsideTheImageAreClamped()
    {
        var image = Chessboard(20, 20);
        var blurred = Redactor.Blur(image, [new RectInt32(-5, -5, 15, 15), new RectInt32(100, 100, 10, 10), new RectInt32(15, 15, 50, 50)]);
        Assert.Equal(image.Width * image.Height * 4, blurred.Pixels.Length);
        Assert.InRange(blurred.Pixels[(2 * 20 + 2) * 4 + 2], 110, 145);
        Assert.Equal(image.GetPixel(12, 3), blurred.GetPixel(12, 3));
    }

    [Fact]
    public void RedactionRendersOnlyTheBlurredAreas()
    {
        var image = Chessboard(80, 40);
        var matches = new[]
        {
            new PersonalInfoMatch(PersonalInfoKind.Email, "a@b.org", new RectInt32(0, 0, 24, 16)),
            new PersonalInfoMatch(PersonalInfoKind.Phone, "555-123-4567", new RectInt32(40, 20, 24, 16)),
        };
        var redaction = new Redaction(image, matches);

        Assert.Equal(2, redaction.BlurredCount);
        Assert.NotEqual(image.GetPixel(1, 0), redaction.Render().GetPixel(1, 0));
        Assert.NotEqual(image.GetPixel(41, 20), redaction.Render().GetPixel(41, 20));

        redaction.Set(1, false);
        Assert.Equal(1, redaction.BlurredCount);
        Assert.NotEqual(image.GetPixel(1, 0), redaction.Render().GetPixel(1, 0));
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
