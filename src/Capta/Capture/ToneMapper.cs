namespace Capta.Capture;

/// <summary>
/// Converts FP16 scRGB captures (linear, Rec.709 primaries, 1.0 = 80 nits) to 8-bit sRGB, or to
/// Display P3 for saved files (keeping wide-gamut colours that sRGB would clip).
/// </summary>
/// <remarks>
/// SDR white on an HDR display sits at the SDR white level, not 80 nits, so pixels are first
/// scaled by 80 / SDR white. A capture with nothing brighter than SDR white then maps exactly
/// (UI white stays #FFFFFF) whatever the roll-off. When there are highlights:
/// <list type="bullet">
/// <item>Clip divides the pixel by its largest channel, keeping hue.</item>
/// <item>Balanced and Soft compress everything above a knee into the space below SDR white with
/// an extended Reinhard curve whose white point is the frame's brightest pixel, applied to the
/// largest channel so hue is kept.</item>
/// </list>
/// Display P3 converts the linear values to P3 primaries first (scRGB holds colours outside
/// Rec.709 as negative components); both spaces use the sRGB tone curve.
/// </remarks>
public static class ToneMapper
{
    private const float ScRgbReferenceNits = 80f;
    private const int SrgbLutSize = 65536;

    private static readonly float[] s_halfToFloat = BuildHalfLut();
    private static readonly byte[] s_linearToSrgb = BuildSrgbLut();

    public static CapturedImage ToSdr(ushort[] rgbaHalf, int width, int height, float sdrWhiteLevelNits,
        HighlightRollOff rollOff = HighlightRollOff.Clip, ColourSpace target = ColourSpace.Srgb)
    {
        var toP3 = target == ColourSpace.DisplayP3;
        var scale = ScRgbReferenceNits / MathF.Max(sdrWhiteLevelNits, 1f);
        var knee = rollOff switch
        {
            HighlightRollOff.Soft => 0.6f,
            HighlightRollOff.Balanced => 0.8f,
            _ => 1f,
        };

        // Only compress when the frame actually has highlights; otherwise SDR stays exact.
        var peak = knee < 1f ? FramePeak(rgbaHalf, scale) : 1f;
        var compress = peak > 1.001f;
        var whiteT = compress ? (peak - knee) / (1 - knee) : 1f;

        var output = new byte[width * height * 4];
        Parallel.For(0, height, y =>
        {
            var src = rgbaHalf.AsSpan(y * width * 4, width * 4);
            var dst = output.AsSpan(y * width * 4, width * 4);
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                var r = s_halfToFloat[src[i]] * scale;
                var g = s_halfToFloat[src[i + 1]] * scale;
                var b = s_halfToFloat[src[i + 2]] * scale;
                if (toP3)
                {
                    // Linear Rec.709 to Display P3 (D65); from tools/generate_display_p3_icc.py.
                    (r, g, b) = (0.8224620f * r + 0.1775380f * g,
                        0.0331942f * r + 0.9668058f * g,
                        0.0170826f * r + 0.0723974f * g + 0.9105199f * b);
                }
                r = MathF.Max(r, 0f);
                g = MathF.Max(g, 0f);
                b = MathF.Max(b, 0f);

                var m = MathF.Max(r, MathF.Max(g, b));
                if (compress && m > knee)
                {
                    var t = (m - knee) / (1 - knee);
                    var curved = t * (1 + t / (whiteT * whiteT)) / (1 + t);
                    var k = (knee + (1 - knee) * MathF.Min(curved, 1f)) / m;
                    r *= k; g *= k; b *= k;
                }
                else if (m > 1f)
                {
                    var k = 1f / m;
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

    /// <summary>Brightest channel value in the frame, relative to SDR white.</summary>
    private static float FramePeak(ushort[] rgbaHalf, float scale)
    {
        var parts = Environment.ProcessorCount;
        var peaks = new float[parts];
        var pixels = rgbaHalf.Length / 4;
        Parallel.For(0, parts, part =>
        {
            var start = (int)((long)pixels * part / parts);
            var end = (int)((long)pixels * (part + 1) / parts);
            var local = 0f;
            for (var p = start; p < end; p++)
            {
                var i = p * 4;
                local = MathF.Max(local, MathF.Max(s_halfToFloat[rgbaHalf[i]], MathF.Max(s_halfToFloat[rgbaHalf[i + 1]], s_halfToFloat[rgbaHalf[i + 2]])));
            }
            peaks[part] = local;
        });
        return peaks.Max() * scale;
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
