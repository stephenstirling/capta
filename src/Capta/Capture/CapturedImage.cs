using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Imaging;

namespace Capta.Capture;

/// <summary>An SDR capture: 8-bit BGRA, opaque, top-down rows with no padding.</summary>
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
        return new CapturedImage(w, h, dst) { HdrPixels = hdr };
    }

    public SoftwareBitmap ToSoftwareBitmap()
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, Width, Height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(Pixels.AsBuffer());
        return bitmap;
    }
}
