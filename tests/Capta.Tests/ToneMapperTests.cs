using Capta.Capture;

namespace Capta.Tests;

public class ToneMapperTests
{
    /// <summary>Maps one scRGB pixel (1.0 = 80 nits) and returns its 8-bit R, G, B.</summary>
    private static (int R, int G, int B) Map(float r, float g, float b, float sdrWhiteNits = 80f,
        HighlightRollOff rollOff = HighlightRollOff.Clip, ColourSpace target = ColourSpace.Srgb)
    {
        var bgra = ToneMapper.ToSdr(Pixels((r, g, b)), 1, 1, sdrWhiteNits, rollOff, target).Pixels;
        return (bgra[2], bgra[1], bgra[0]);
    }

    private static ushort[] Pixels(params (float R, float G, float B)[] pixels) =>
        pixels.SelectMany(p => new[] { Half(p.R), Half(p.G), Half(p.B), Half(1) }).ToArray();

    private static ushort Half(float value) => BitConverter.HalfToUInt16Bits((Half)value);

    private static float SrgbToLinear(int value)
    {
        var s = value / 255.0;
        return (float)(s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4));
    }

    [Fact]
    public void SdrScreenRoundTripsEveryLevel()
    {
        // An SDR desktop arrives as the sRGB values decoded to linear FP16; they must come back exactly.
        for (var v = 0; v < 256; v++)
        {
            var linear = SrgbToLinear(v);
            Assert.Equal((v, v, v), Map(linear, linear, linear));
        }
    }

    [Fact]
    public void SdrWhiteOnAnHdrScreenIsWhite()
    {
        // At 240 nits SDR white, UI white is 3.0 in scRGB.
        Assert.Equal((255, 255, 255), Map(3f, 3f, 3f, sdrWhiteNits: 240f));
        Assert.Equal((188, 188, 188), Map(1.5f, 1.5f, 1.5f, sdrWhiteNits: 240f));
    }

    [Fact]
    public void ClipKeepsHue()
    {
        // Twice SDR white in red, once in green: scaled down together, not clipped per channel.
        Assert.Equal((255, 188, 0), Map(2f, 1f, 0f));
    }

    [Theory]
    [InlineData(HighlightRollOff.Balanced)]
    [InlineData(HighlightRollOff.Soft)]
    public void RollOffLeavesFramesWithoutHighlightsExact(HighlightRollOff rollOff)
    {
        var pixels = Pixels((1f, 1f, 1f), (0.5f, 0.25f, 0.1f));
        Assert.Equal(
            ToneMapper.ToSdr(pixels, 2, 1, 80f, HighlightRollOff.Clip).Pixels,
            ToneMapper.ToSdr(pixels, 2, 1, 80f, rollOff).Pixels);
    }

    [Theory]
    [InlineData(HighlightRollOff.Balanced)]
    [InlineData(HighlightRollOff.Soft)]
    public void RollOffCompressesBelowWhiteWhenThereAreHighlights(HighlightRollOff rollOff)
    {
        // A highlight at 4x SDR white: the brightest pixel still reaches white, and a pixel just
        // under white is pulled down to make room.
        var bgra = ToneMapper.ToSdr(Pixels((4f, 4f, 4f), (0.95f, 0.95f, 0.95f)), 2, 1, 80f, rollOff).Pixels;
        Assert.Equal(255, bgra[2]);
        Assert.InRange(bgra[6], 1, ToneMapper.ToSdr(Pixels((0.95f, 0.95f, 0.95f)), 1, 1, 80f).Pixels[2] - 1);
    }

    [Fact]
    public void OutputIsOpaque()
    {
        var bgra = ToneMapper.ToSdr(Pixels((0f, 0f, 0f), (0.5f, 0.5f, 0.5f)), 2, 1, 80f).Pixels;
        Assert.Equal(255, bgra[3]);
        Assert.Equal(255, bgra[7]);
    }

    [Theory]
    // sRGB primaries in Display P3 (the values every colour engine gives).
    [InlineData(1f, 0f, 0f, 234, 51, 35)]
    [InlineData(0f, 1f, 0f, 117, 251, 76)]
    [InlineData(0f, 0f, 1f, 0, 0, 245)]
    [InlineData(1f, 1f, 1f, 255, 255, 255)]
    [InlineData(0f, 0f, 0f, 0, 0, 0)]
    public void DisplayP3ConvertsPrimaries(float r, float g, float b, int p3R, int p3G, int p3B) =>
        Assert.Equal((p3R, p3G, p3B), Map(r, g, b, target: ColourSpace.DisplayP3));

    [Fact]
    public void DisplayP3KeepsColoursOutsideSrgb()
    {
        // Display P3 red written in scRGB: outside Rec.709, so negative green and blue.
        var (r, g, b) = (1.22494f, -0.04206f, -0.01964f);
        var p3 = Map(r, g, b, target: ColourSpace.DisplayP3);
        Assert.Equal(255, p3.R);
        Assert.InRange(p3.G, 0, 2);
        Assert.InRange(p3.B, 0, 2);
        // sRGB can only clip it to its own red.
        Assert.Equal((255, 0, 0), Map(r, g, b));
    }

    [Theory]
    [InlineData(HighlightRollOff.Balanced)]
    [InlineData(HighlightRollOff.Soft)]
    public void TheBrightestColourReachesFullBrightnessInDisplayP3(HighlightRollOff rollOff)
    {
        // The frame's peak is a saturated red, whose largest channel is smaller in P3 than in
        // Rec.709: the roll-off must still take it to full brightness.
        var bgra = ToneMapper.ToSdr(Pixels((4f, 0f, 0f), (0.5f, 0.5f, 0.5f)), 2, 1, 80f, rollOff, ColourSpace.DisplayP3).Pixels;
        Assert.Equal(255, bgra[2]);
    }

    [Fact]
    public void PeaksAreMeasuredInBothSpaces()
    {
        var peaks = ToneMapper.MeasurePeaks(Pixels((4f, 0f, 0f), (0.5f, 0.5f, 0.5f)));
        Assert.Equal(4f, peaks.Rec709);
        Assert.Equal(4f * 0.822462f, peaks.DisplayP3, 0.001f);
    }

    [Fact]
    public void GivenPeaksReplaceTheMeasuredOnes()
    {
        // A crop without the highlight, mapped with the whole frame's peaks, is compressed as before.
        var pixel = Pixels((0.95f, 0.95f, 0.95f));
        var alone = ToneMapper.ToSdr(pixel, 1, 1, 80f, HighlightRollOff.Balanced).Pixels[2];
        var inFrame = ToneMapper.ToSdr(pixel, 1, 1, 80f, HighlightRollOff.Balanced, peaks: new ToneMapper.FramePeaks(4f, 4f)).Pixels[2];
        Assert.True(inFrame < alone, $"{inFrame} should be below {alone}");
    }

    [Fact]
    public void GreysAreTheSameInBothSpaces()
    {
        for (var v = 0; v < 256; v += 15)
        {
            var linear = SrgbToLinear(v);
            Assert.Equal(Map(linear, linear, linear), Map(linear, linear, linear, target: ColourSpace.DisplayP3));
        }
    }
}
