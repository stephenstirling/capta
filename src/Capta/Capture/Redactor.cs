using Windows.Graphics;

namespace Capta.Capture;

/// <summary>
/// Covers areas of a capture with a solid black fill, so what was there can't be read back (a blur or
/// mosaic leaves traces of it). The HDR original is covered too, so Display P3 and HDR output are
/// redacted as well.
/// </summary>
public static class Redactor
{
    /// <summary>Half-float 1.0, the opaque alpha of the HDR frame.</summary>
    private static readonly ushort s_halfOne = BitConverter.HalfToUInt16Bits((Half)1f);

    /// <summary>
    /// A copy of <paramref name="image"/> with <paramref name="areas"/> (image pixels, clamped to the
    /// image) filled with opaque black. The original is left alone.
    /// </summary>
    public static CapturedImage Fill(CapturedImage image, IEnumerable<RectInt32> areas)
    {
        var sdr = (byte[])image.Pixels.Clone();
        var hdr = (ushort[]?)image.HdrPixels?.Clone();
        foreach (var area in areas)
        {
            var x0 = Math.Clamp(area.X, 0, image.Width);
            var y0 = Math.Clamp(area.Y, 0, image.Height);
            var x1 = Math.Clamp(area.X + area.Width, x0, image.Width);
            var y1 = Math.Clamp(area.Y + area.Height, y0, image.Height);
            for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                var i = (y * image.Width + x) * 4;
                sdr[i] = sdr[i + 1] = sdr[i + 2] = 0;
                sdr[i + 3] = 0xFF;
                if (hdr is null) continue;
                hdr[i] = hdr[i + 1] = hdr[i + 2] = 0;
                hdr[i + 3] = s_halfOne;
            }
        }
        // A fill over a transparent (freeform or window-corner) pixel makes it opaque.
        return image.WithPixels(sdr, hdr);
    }
}
