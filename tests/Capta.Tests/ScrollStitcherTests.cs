using Capta.Capture;

namespace Capta.Tests;

public class ScrollStitcherTests
{
    private const int Width = 64;          // the rightmost 24 columns are the scrollbar
    private const int Header = 10;         // sticky rows at the top
    private const int Footer = 8;          // and at the bottom
    private const int Band = 82;           // rows of content visible at once
    private const int FrameHeight = Header + Band + Footer;
    private const int ContentRows = 250;
    private const int HeaderSeed = 100_000; // row seeds: content rows use 0 to ContentRows - 1
    private const int FooterSeed = 200_000;

    /// <summary>A row of colour that no other row repeats, in the columns the stitcher compares.</summary>
    private static byte[] Row(int seed)
    {
        var row = new byte[Width * 4];
        var random = new Random(seed);
        random.NextBytes(row);
        for (var i = 3; i < row.Length; i += 4) row[i] = 255;
        return row;
    }

    /// <summary>The window scrolled down by <paramref name="offset"/> rows, with a moving scrollbar.</summary>
    private static CapturedImage Frame(int offset)
    {
        var pixels = new byte[Width * FrameHeight * 4];
        for (var y = 0; y < FrameHeight; y++)
        {
            var seed = y < Header ? HeaderSeed + y
                : y >= Header + Band ? FooterSeed + y
                : offset + y - Header;
            Row(seed).CopyTo(pixels, y * Width * 4);
            // The scrollbar thumb is different in every frame.
            var scrollbar = new Random(offset * 1000 + y);
            for (var x = Width - 24; x < Width; x++)
                pixels[(y * Width + x) * 4] = (byte)scrollbar.Next(256);
        }
        return new CapturedImage(Width, FrameHeight, pixels) { SourceProfile = [9] };
    }

    /// <summary>Compares rows, leaving out the scrollbar columns.</summary>
    private static void AssertRow(byte[] expected, CapturedImage image, int y) =>
        Assert.Equal(expected[..((Width - 24) * 4)], image.Pixels[(y * Width * 4)..((y * Width + Width - 24) * 4)]);

    [Fact]
    public void StitchesTheScrolledContentOnceWithOneHeaderAndFooter()
    {
        var stitcher = new ScrollStitcher(Frame(0));
        var offsets = new[] { 30, 60, 90, 120, 150, ContentRows - Band };
        var previous = 0;
        foreach (var offset in offsets)
        {
            Assert.Equal(offset - previous, stitcher.Add(Frame(offset)));
            previous = offset;
        }
        Assert.Equal(0, stitcher.Add(Frame(previous))); // the end: nothing moved

        var image = stitcher.Build();

        Assert.Equal(Header + ContentRows + Footer, image.Height);
        Assert.Equal(image.Height, stitcher.Height);
        for (var y = 0; y < Header; y++) AssertRow(Row(HeaderSeed + y), image, y);
        for (var y = 0; y < ContentRows; y++) AssertRow(Row(y), image, Header + y);
        for (var y = 0; y < Footer; y++) AssertRow(Row(FooterSeed + Header + Band + y), image, Header + ContentRows + y);
        Assert.Equal(new byte[] { 9 }, image.SourceProfile);
    }

    [Fact]
    public void AFrameOfAnotherSizeDoesNotMatch() =>
        Assert.Equal(-1, new ScrollStitcher(Frame(0)).Add(new CapturedImage(Width, 10, new byte[Width * 10 * 4])));

    [Fact]
    public void UnrelatedContentDoesNotMatch()
    {
        var stitcher = new ScrollStitcher(Frame(0));
        var other = Frame(0);
        for (var y = Header; y < Header + Band; y++)
            Row(50_000 + y).CopyTo(other.Pixels, y * Width * 4);
        Assert.Equal(-1, stitcher.Add(other));
    }

    [Fact]
    public void WithoutScrollingTheImageIsTheFirstFrame()
    {
        var first = Frame(0);
        var stitcher = new ScrollStitcher(first);
        Assert.Equal(0, stitcher.Add(Frame(0)));
        Assert.Same(first, stitcher.Build());
    }
}
