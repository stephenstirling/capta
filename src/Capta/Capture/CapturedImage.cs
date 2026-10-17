using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Imaging;

namespace Capta.Capture;

/// <summary>
/// An SDR capture: 8-bit BGRA (premultiplied), top-down rows with no padding. Opaque unless
/// <see cref="HasTransparency"/> (freeform captures are transparent outside their shape).
/// </summary>
/// <remarks>
/// The pixels are what the screen showed, for display inside Capta. Anything leaving Capta goes
/// through <see cref="ColourOutput"/>, which applies <see cref="SourceProfile"/> and the chosen
/// colour space.
/// </remarks>
public sealed class CapturedImage
{
    public CapturedImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer does not match dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public bool HasTransparency { get; init; }

    /// <summary>
    /// The original HDR pixels (RGBA half floats, scRGB, same size) when the source display was in
    /// HDR, for "Copy image as HDR" and Keep HDR saves. Null for SDR captures.
    /// </summary>
    public ushort[]? HdrPixels
    {
        get;
        init
        {
            if (value is not null && value.Length != Width * Height * 4)
                throw new ArgumentException("HDR buffer does not match dimensions.", nameof(value));
            field = value;
        }
    }

    /// <summary>How <see cref="HdrPixels"/> was tone-mapped, so Display P3 output maps it the same way.</summary>
    public HdrToneMap? ToneMap { get; init; }

    /// <summary>
    /// ICC profile of the SDR display the pixels came from, when they should be corrected for it on
    /// the way out (Colour &amp; HDR → "Correct for each monitor's colour profile"). Null: sRGB.
    /// </summary>
    public byte[]? SourceProfile { get; init; }

    public uint GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        return BitConverter.ToUInt32(Pixels, (y * Width + x) * 4);
    }

    /// <summary>Copies a sub-rectangle (clamped to the image bounds).</summary>
    public CapturedImage Crop(RectInt32 rect)
    {
        var x0 = Math.Clamp(rect.X, 0, Width);
        var y0 = Math.Clamp(rect.Y, 0, Height);
        var x1 = Math.Clamp(rect.X + rect.Width, x0, Width);
        var y1 = Math.Clamp(rect.Y + rect.Height, y0, Height);
        int w = x1 - x0, h = y1 - y0;
        var dst = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(Pixels, ((y0 + row) * Width + x0) * 4, dst, row * w * 4, w * 4);

        ushort[]? hdr = null;
        if (HdrPixels is not null)
        {
            hdr = new ushort[w * h * 4];
            for (var row = 0; row < h; row++)
                Array.Copy(HdrPixels, ((y0 + row) * Width + x0) * 4, hdr, row * w * 4, w * 4);
        }
        return Derive(w, h, dst, hdr, HasTransparency);
    }

    /// <summary>
    /// A copy with every pixel outside <paramref name="polygon"/> (image coordinates, even-odd
    /// rule, sampled at pixel centres) made fully transparent, in the HDR original too.
    /// </summary>
    public CapturedImage MaskOutside(IReadOnlyList<PointInt32> polygon)
    {
        var sdr = (byte[])Pixels.Clone();
        var hdr = (ushort[]?)HdrPixels?.Clone();
        var crossings = new List<double>();
        for (var y = 0; y < Height; y++)
        {
            // Scanline fill: x positions where the edges cross this row's centre line.
            var cy = y + 0.5;
            crossings.Clear();
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                double y0 = polygon[j].Y + 0.5, y1 = polygon[i].Y + 0.5;
                if ((y0 <= cy) == (y1 <= cy)) continue;
                double x0 = polygon[j].X + 0.5, x1 = polygon[i].X + 0.5;
                crossings.Add(x0 + (cy - y0) / (y1 - y0) * (x1 - x0));
            }
            crossings.Sort();

            var k = 0;
            var inside = false;
            for (var x = 0; x < Width; x++)
            {
                var cx = x + 0.5;
                while (k < crossings.Count && crossings[k] <= cx)
                {
                    inside = !inside;
                    k++;
                }
                if (inside) continue;
                Array.Clear(sdr, (y * Width + x) * 4, 4);
                if (hdr is not null) Array.Clear(hdr, (y * Width + x) * 4, 4);
            }
        }
        return Derive(Width, Height, sdr, hdr, transparent: true);
    }

    /// <summary>
    /// The SDR pixels at twice the size, bilinear (pixel centres aligned). Only for analysis such as OCR,
    /// which reads small screen text better when it's bigger; the result carries no HDR or colour info.
    /// </summary>
    public CapturedImage Upscale2x()
    {
        int w = Width * 2, h = Height * 2;
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            // Source position of this pixel's centre, clamped to the edge pixels.
            var sy = Math.Clamp((y + 0.5) / 2 - 0.5, 0, Height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, Height - 1);
            var fy = sy - y0;
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Clamp((x + 0.5) / 2 - 0.5, 0, Width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, Width - 1);
                var fx = sx - x0;
                int a = (y0 * Width + x0) * 4, b = (y0 * Width + x1) * 4, c = (y1 * Width + x0) * 4, d = (y1 * Width + x1) * 4;
                var o = (y * w + x) * 4;
                for (var ch = 0; ch < 4; ch++)
                {
                    var top = Pixels[a + ch] + (Pixels[b + ch] - Pixels[a + ch]) * fx;
                    var bottom = Pixels[c + ch] + (Pixels[d + ch] - Pixels[c + ch]) * fx;
                    dst[o + ch] = (byte)Math.Round(top + (bottom - top) * fy);
                }
            }
        }
        return new CapturedImage(w, h, dst);
    }

    /// <summary>A copy of this image with other pixels (same size), keeping how its colours are to be output.</summary>
    public CapturedImage WithPixels(byte[] pixels, ushort[]? hdrPixels) =>
        Derive(Width, Height, pixels, hdrPixels, HasTransparency);

    /// <summary>A new image from this one's pixels, keeping how its colours are to be output.</summary>
    private CapturedImage Derive(int width, int height, byte[] pixels, ushort[]? hdr, bool transparent) =>
        new(width, height, pixels)
        {
            HdrPixels = hdr,
            HasTransparency = transparent,
            ToneMap = ToneMap,
            SourceProfile = SourceProfile,
        };

    public SoftwareBitmap ToSoftwareBitmap()
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, Width, Height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(Pixels.AsBuffer());
        return bitmap;
    }
}

/// <param name="SdrWhiteNits">The SDR white level the HDR original was mapped with.</param>
/// <param name="Peaks">The whole frame's peaks, so a crop's highlights roll off as they did in the frame
/// (null when the roll-off is Clip, which doesn't use them).</param>
public sealed record HdrToneMap(float SdrWhiteNits, HighlightRollOff RollOff, ToneMapper.FramePeaks? Peaks);
