using Windows.Graphics;

namespace Capta.Capture;

/// <summary>
/// Blurs areas of a capture so what was there can't be read back: each area is reduced to a
/// mosaic of blocks about half a line of text high, then softened. A plain Gaussian blur of small
/// text can be sharpened again; averaging into blocks throws that detail away.
/// </summary>
public static class Redactor
{
    private const int MinBlock = 8;

    /// <summary>
    /// A copy of <paramref name="image"/> with <paramref name="areas"/> (image pixels, clamped to the
    /// image) blurred, in the HDR original too, so Display P3 and HDR output are blurred as well.
    /// </summary>
    public static CapturedImage Blur(CapturedImage image, IEnumerable<RectInt32> areas)
    {
        var sdr = (byte[])image.Pixels.Clone();
        var hdr = (ushort[]?)image.HdrPixels?.Clone();
        foreach (var area in areas)
        {
            var x0 = Math.Clamp(area.X, 0, image.Width);
            var y0 = Math.Clamp(area.Y, 0, image.Height);
            var x1 = Math.Clamp(area.X + area.Width, x0, image.Width);
            var y1 = Math.Clamp(area.Y + area.Height, y0, image.Height);
            int w = x1 - x0, h = y1 - y0;
            if (w == 0 || h == 0) continue;

            var block = Math.Max(MinBlock, h / 2);
            BlurRegion(sdr, image.Width, x0, y0, w, h, block, ByteToFloat, FloatToByte);
            if (hdr is not null)
                BlurRegion(hdr, image.Width, x0, y0, w, h, block, HalfToFloat, FloatToHalf);
        }
        return image.WithPixels(sdr, hdr);
    }

    private static float ByteToFloat(byte v) => v;

    private static byte FloatToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    private static float HalfToFloat(ushort v) => (float)BitConverter.UInt16BitsToHalf(v);

    private static ushort FloatToHalf(float v) => BitConverter.HalfToUInt16Bits((Half)v);

    /// <summary>Mosaic then a 3 × 3 average over the region of an RGBA/BGRA buffer.</summary>
    private static void BlurRegion<T>(T[] pixels, int stride, int x0, int y0, int w, int h, int block,
        Func<T, float> read, Func<float, T> write)
    {
        var region = new float[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var c = 0; c < 4; c++)
            region[(y * w + x) * 4 + c] = read(pixels[((y0 + y) * stride + x0 + x) * 4 + c]);

        // Mosaic: every pixel becomes the average of its block (blocks start at the area's corner).
        var sum = new float[4];
        for (var by = 0; by < h; by += block)
        for (var bx = 0; bx < w; bx += block)
        {
            int bw = Math.Min(block, w - bx), bh = Math.Min(block, h - by);
            Array.Clear(sum);
            for (var y = by; y < by + bh; y++)
            for (var x = bx; x < bx + bw; x++)
            for (var c = 0; c < 4; c++)
                sum[c] += region[(y * w + x) * 4 + c];
            for (var c = 0; c < 4; c++) sum[c] /= bw * bh;
            for (var y = by; y < by + bh; y++)
            for (var x = bx; x < bx + bw; x++)
            for (var c = 0; c < 4; c++)
                region[(y * w + x) * 4 + c] = sum[c];
        }

        // Soften the block edges.
        var soft = new float[region.Length];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var c = 0; c < 4; c++)
        {
            float total = 0;
            var n = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int sx = x + dx, sy = y + dy;
                if ((uint)sx >= (uint)w || (uint)sy >= (uint)h) continue;
                total += region[(sy * w + sx) * 4 + c];
                n++;
            }
            soft[(y * w + x) * 4 + c] = total / n;
        }

        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        for (var c = 0; c < 4; c++)
            pixels[((y0 + y) * stride + x0 + x) * 4 + c] = write(soft[(y * w + x) * 4 + c]);
    }
}
