using Capta.Capture;
using Windows.Graphics;

namespace Capta.Tests;

public class CapturedImageTests
{
    private static readonly HdrToneMap ToneMap = new(240f, HighlightRollOff.Soft, new ToneMapper.FramePeaks(4f, 3.5f));

    /// <summary>A width × height image whose pixel (x, y) is B = x, G = y, R = 7, A = 255.</summary>
    private static CapturedImage Gradient(int width, int height, bool withHdr = false)
    {
        var pixels = new byte[width * height * 4];
        var hdr = withHdr ? new ushort[width * height * 4] : null;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)x, (byte)y, 7, 255);
            if (hdr is not null) hdr[i] = (ushort)(1000 + y * width + x);
        }
        return new CapturedImage(width, height, pixels)
        {
            HdrPixels = hdr,
            ToneMap = ToneMap,
            SourceProfile = withHdr ? null : [1, 2, 3],
        };
    }

    private static (int X, int Y) At(CapturedImage image, int x, int y)
    {
        var pixel = image.GetPixel(x, y);
        return ((int)(pixel & 0xFF), (int)(pixel >> 8 & 0xFF));
    }

    [Fact]
    public void RejectsAPixelBufferOfTheWrongSize() =>
        Assert.Throws<ArgumentException>(() => new CapturedImage(2, 2, new byte[15]));

    [Fact]
    public void PixelsOutsideTheImageReadAsZero()
    {
        var image = Gradient(3, 3);
        Assert.Equal(0u, image.GetPixel(-1, 0));
        Assert.Equal(0u, image.GetPixel(3, 0));
        Assert.Equal(0u, image.GetPixel(0, 3));
    }

    [Fact]
    public void CropCopiesTheRectangleAndKeepsColourDetails()
    {
        var crop = Gradient(10, 8).Crop(new RectInt32(2, 3, 4, 2));

        Assert.Equal((4, 2), (crop.Width, crop.Height));
        Assert.Equal((2, 3), At(crop, 0, 0));
        Assert.Equal((5, 4), At(crop, 3, 1));
        Assert.Same(ToneMap, crop.ToneMap);
        Assert.Equal(new byte[] { 1, 2, 3 }, crop.SourceProfile);
    }

    [Fact]
    public void CropIsClampedToTheImage()
    {
        var crop = Gradient(10, 8).Crop(new RectInt32(-5, 6, 8, 10));
        Assert.Equal((3, 2), (crop.Width, crop.Height));
        Assert.Equal((0, 6), At(crop, 0, 0));
    }

    [Fact]
    public void CropCutsTheHdrOriginalToo()
    {
        var crop = Gradient(10, 8, withHdr: true).Crop(new RectInt32(2, 3, 4, 2));
        Assert.NotNull(crop.HdrPixels);
        Assert.Equal(4 * 2 * 4, crop.HdrPixels!.Length);
        Assert.Equal(1000 + 3 * 10 + 2, crop.HdrPixels[0]);
        Assert.Equal(1000 + 4 * 10 + 5, crop.HdrPixels[(1 * 4 + 3) * 4]);
    }

    [Fact]
    public void MaskOutsideClearsEverythingOutsideTheShape()
    {
        var image = Gradient(10, 10, withHdr: true);
        // A square from (2, 2) to (6, 6): pixel centres 2.5 to 5.5 are inside.
        var masked = image.MaskOutside([new(2, 2), new(6, 2), new(6, 6), new(2, 6)]);

        Assert.True(masked.HasTransparency);
        for (var y = 0; y < 10; y++)
        for (var x = 0; x < 10; x++)
        {
            var inside = x is >= 2 and < 6 && y is >= 2 and < 6;
            Assert.Equal(inside ? image.GetPixel(x, y) : 0u, masked.GetPixel(x, y));
            Assert.Equal(inside ? image.HdrPixels![(y * 10 + x) * 4] : (ushort)0, masked.HdrPixels![(y * 10 + x) * 4]);
        }
        Assert.Same(ToneMap, masked.ToneMap);
        Assert.NotSame(image.Pixels, masked.Pixels); // the original is left alone
    }

    [Fact]
    public void MaskOutsideFollowsATriangle()
    {
        var masked = Gradient(10, 10).MaskOutside([new(0, 0), new(10, 0), new(0, 10)]);
        Assert.NotEqual(0u, masked.GetPixel(1, 1));
        Assert.NotEqual(0u, masked.GetPixel(7, 1));
        Assert.Equal(0u, masked.GetPixel(8, 8));
        Assert.Equal(0u, masked.GetPixel(5, 5)); // centre (5.5, 5.5) is past the diagonal
    }
}
