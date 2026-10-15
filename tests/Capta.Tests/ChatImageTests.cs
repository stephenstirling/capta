using Capta.Capture;

namespace Capta.Tests;

public class ChatImageTests
{
    [Theory]
    [InlineData(4000, 2500, 1568, 980)]   // landscape: the width is the long edge
    [InlineData(1000, 3000, 523, 1568)]   // portrait: the height is the long edge (1000 × 1568 / 3000 = 522.7)
    [InlineData(3840, 2160, 1568, 882)]
    [InlineData(1569, 1569, 1568, 1568)]  // just over: square
    [InlineData(5000, 10, 1568, 3)]       // extreme ratios keep at least one pixel
    [InlineData(10_000, 1, 1568, 1)]
    public void ScalesTheLongEdgeDownToTheLimit(int width, int height, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), ChatImage.ScaledSize(width, height));

    [Theory]
    [InlineData(1568, 1000)]  // exactly the limit
    [InlineData(800, 600)]
    [InlineData(1, 1)]
    [InlineData(1568, 1568)]
    public void NeverScalesUp(int width, int height) =>
        Assert.Equal((width, height), ChatImage.ScaledSize(width, height));

    [Fact]
    public void TheLimitCanBeChanged() =>
        Assert.Equal((1000, 500), ChatImage.ScaledSize(2000, 1000, maxEdge: 1000));

    [Theory]
    [InlineData(0, false)]
    [InlineData(412 * 1024, false)]
    [InlineData(1024 * 1024, false)]     // 1 MB exactly still goes as a PNG
    [InlineData(1024 * 1024 + 1, true)]  // over 1 MB: JPEG
    [InlineData(5 * 1024 * 1024, true)]
    public void AJpegReplacesAPngOverOneMegabyte(long pngBytes, bool expectedJpeg) =>
        Assert.Equal(expectedJpeg, ChatImage.UseJpeg(pngBytes));

    [Fact]
    public void QualityIs85() => Assert.Equal(85, ChatImage.JpegQuality);

    [Theory]
    [InlineData(412 * 1024, "412 KB")]
    [InlineData(412 * 1024 + 511, "412 KB")]
    [InlineData(412 * 1024 + 512, "413 KB")]
    [InlineData(100, "1 KB")]
    [InlineData(0, "1 KB")]
    public void FormatsSizesInKilobytes(long bytes, string expected) =>
        Assert.Equal(expected, ChatImage.FormatSize(bytes));

    [Fact]
    public void TheToastReadsLikeTheDesign() =>
        Assert.Equal("Copied for chat · 1568 × 980 · 412 KB", new ChatCopy(1568, 980, 412 * 1024, IsJpeg: false).Describe());

    [Fact]
    public void DownscaleAveragesEachBlock()
    {
        // 4 × 2, scaled to 2 × 1: each new pixel is the mean of a 2 × 2 block.
        byte[] pixels =
        [
            0, 0, 0, 255, 100, 100, 100, 255, 200, 40, 0, 255, 200, 40, 0, 255,
            100, 100, 100, 255, 200, 200, 200, 255, 200, 40, 0, 255, 200, 40, 0, 255,
        ];
        var scaled = ChatImage.Downscale(pixels, 4, 2, 2, 1);
        Assert.Equal(new byte[] { 100, 100, 100, 255, 200, 40, 0, 255 }, scaled);
    }

    [Fact]
    public void DownscaleKeepsAFlatImageFlat()
    {
        var pixels = new byte[30 * 20 * 4];
        for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (10, 120, 250, 255);
        var scaled = ChatImage.Downscale(pixels, 30, 20, 7, 5);
        Assert.Equal(7 * 5 * 4, scaled.Length);
        for (var i = 0; i < scaled.Length; i += 4)
            Assert.Equal(new byte[] { 10, 120, 250, 255 }, scaled[i..(i + 4)]);
    }

    [Fact]
    public void DownscaleAveragesAlphaToo()
    {
        // One opaque white pixel and one transparent one (premultiplied zero) average to half alpha.
        var scaled = ChatImage.Downscale([255, 255, 255, 255, 0, 0, 0, 0], 2, 1, 1, 1);
        Assert.Equal(new byte[] { 128, 128, 128, 128 }, scaled);
    }

    [Fact]
    public void DownscaleLeavesAnUnchangedSizeAlone()
    {
        var pixels = new byte[16];
        Assert.Same(pixels, ChatImage.Downscale(pixels, 2, 2, 2, 2));
    }

    [Fact]
    public void DownscaleRefusesToScaleUp() =>
        Assert.Throws<ArgumentException>(() => ChatImage.Downscale(new byte[16], 2, 2, 4, 4));

    [Fact]
    public void FlatteningPutsTransparentAreasOnWhite()
    {
        // Opaque red stays; transparent becomes white; half-transparent black (premultiplied 0, alpha 128) becomes mid grey.
        var flat = ChatImage.FlattenOnWhite([0, 0, 255, 255, 0, 0, 0, 0, 0, 0, 0, 128]);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 255, 255, 255, 127, 127, 127, 255 }, flat);
    }
}
