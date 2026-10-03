namespace Capta.Capture;

/// <summary>
/// Converts FP16 scRGB captures (linear, Rec.709 primaries, 1.0 = 80 nits) to 8-bit sRGB.
/// </summary>
/// <remarks>
/// SDR white on an HDR display sits at SdrWhiteLevelInNits, not 80 nits, so pixels are
/// first scaled by 80 / SdrWhiteLevelInNits. Everything at or below SDR white then maps
/// exactly (UI white stays #FFFFFF). Highlights above it are clipped hue-preservingly by
/// dividing the whole pixel by its largest channel rather than clamping channels apart.
/// </remarks>
public static class ToneMapper
{
    private const float ScRgbReferenceNits = 80f;
    private const int SrgbLutSize = 65536;

    private static readonly float[] s_halfToFloat = BuildHalfLut();
    private static readonly byte[] s_linearToSrgb = BuildSrgbLut();

    public static CapturedImage ToSdr(ushort[] rgbaHalf, int width, int height, float sdrWhiteLevelNits)
    {
        var scale = ScRgbReferenceNits / MathF.Max(sdrWhiteLevelNits, 1f);
        var output = new byte[width * height * 4];

        Parallel.For(0, height, y =>
        {
            var src = rgbaHalf.AsSpan(y * width * 4, width * 4);
            var dst = output.AsSpan(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var r = MathF.Max(s_halfToFloat[src[i]] * scale, 0f);
                var g = MathF.Max(s_halfToFloat[src[i + 1]] * scale, 0f);
                var b = MathF.Max(s_halfToFloat[src[i + 2]] * scale, 0f);

                var peak = MathF.Max(r, MathF.Max(g, b));
                if (peak > 1f)
                {
                    var k = 1f / peak;
                    r *= k; g *= k; b *= k;
                }

                dst[i] = Encode(b);
                dst[i + 1] = Encode(g);
                dst[i + 2] = Encode(r);
                dst[i + 3] = 0xFF;
            }
        });

        return new CapturedImage(width, height, output);
    }

    private static byte Encode(float linear) =>
        s_linearToSrgb[(int)(MathF.Min(linear, 1f) * (SrgbLutSize - 1) + 0.5f)];

    private static float[] BuildHalfLut()
    {
        var lut = new float[65536];
        for (var i = 0; i < lut.Length; i++)
        {
            var f = (float)BitConverter.UInt16BitsToHalf((ushort)i);
            lut[i] = float.IsFinite(f) ? f : 0f;
        }
        return lut;
    }

    private static byte[] BuildSrgbLut()
    {
        var lut = new byte[SrgbLutSize];
        for (var i = 0; i < lut.Length; i++)
        {
            var l = i / (double)(SrgbLutSize - 1);
            var s = l <= 0.0031308 ? 12.92 * l : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
            lut[i] = (byte)Math.Clamp((int)Math.Round(s * 255), 0, 255);
        }
        return lut;
    }
}
