using Capta.Capture;

namespace Capta.Tests;

public class ColourOutputTests
{
    [Fact]
    public void TheDisplayP3ProfileIsEmbedded()
    {
        var profile = ColourOutput.DisplayP3Profile;
        Assert.Equal("acsp"u8.ToArray(), profile[36..40]);
        Assert.Equal((uint)profile.Length, (uint)(profile[0] << 24 | profile[1] << 16 | profile[2] << 8 | profile[3]));
        Assert.Equal("mntr"u8.ToArray(), profile[12..16]);
    }

    [Fact]
    public async Task SrgbWithoutAProfileIsTheCaptureAsIs()
    {
        var image = new CapturedImage(1, 1, [10, 20, 30, 255]);
        var (pixels, space) = await ColourOutput.ConvertAsync(image, ColourSpace.Srgb);
        Assert.Same(image.Pixels, pixels);
        Assert.Equal(ColourSpace.Srgb, space);
    }

    [Fact]
    public async Task HdrCapturesAreToneMappedAgainForDisplayP3()
    {
        // A freeform HDR capture: one red pixel inside the shape, one cleared outside it.
        ushort one = BitConverter.HalfToUInt16Bits((Half)1f);
        var image = new CapturedImage(2, 1, [0, 0, 255, 255, 0, 0, 0, 0])
        {
            HdrPixels = [one, 0, 0, one, 0, 0, 0, 0], // RGBA half floats
            HasTransparency = true,
            ToneMap = new HdrToneMap(80f, HighlightRollOff.Clip, null),
        };

        var (pixels, space) = await ColourOutput.ConvertAsync(image, ColourSpace.DisplayP3);

        Assert.Equal(ColourSpace.DisplayP3, space);
        Assert.Equal(new byte[] { 35, 51, 234, 255, 0, 0, 0, 0 }, pixels);
        // The sRGB pixels shown in Capta are untouched.
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 0, 0 }, image.Pixels);
    }

    [Theory]
    [InlineData(HighlightRollOff.Balanced)]
    [InlineData(HighlightRollOff.Soft)]
    public async Task ACropRollsOffAgainstTheWholeFramesHighlights(HighlightRollOff rollOff)
    {
        // A frame with a highlight at 4x SDR white and a pixel just under white. Cropping out the
        // highlight mustn't change how the remaining pixel is mapped to Display P3.
        ushort Half(float v) => BitConverter.HalfToUInt16Bits((Half)v);
        ushort[] hdr = [Half(4f), Half(4f), Half(4f), Half(1f), Half(0.95f), Half(0.95f), Half(0.95f), Half(1f)];
        var peaks = ToneMapper.MeasurePeaks(hdr);
        var frame = new CapturedImage(2, 1, new byte[8])
        {
            HdrPixels = hdr,
            ToneMap = new HdrToneMap(80f, rollOff, peaks),
        };

        var (whole, _) = await ColourOutput.ConvertAsync(frame, ColourSpace.DisplayP3);
        var (crop, _) = await ColourOutput.ConvertAsync(frame.Crop(new Windows.Graphics.RectInt32(1, 0, 1, 1)), ColourSpace.DisplayP3);

        Assert.Equal(whole[4..7], crop[0..3]);
    }

    [Fact]
    public void KeepAlphaCopiesAlphaAndClearsTransparentPixels()
    {
        var converted = new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 };
        var original = new byte[] { 9, 9, 9, 255, 0, 0, 0, 0 };
        Assert.Equal(new byte[] { 1, 2, 3, 255, 0, 0, 0, 0 }, ColourOutput.KeepAlpha(converted, original));
    }
}
