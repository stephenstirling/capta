namespace Capta.Capture;

/// <summary>What "Copy for chat" put on the clipboard.</summary>
public readonly record struct ChatCopy(int Width, int Height, long Bytes, bool IsJpeg)
{
    /// <summary>"Copied for chat · 1568 × 980 · 412 KB"</summary>
    public string Describe() => ChatImage.Describe(Width, Height, Bytes);
}

/// <summary>
/// The size rules and pixel work for "Copy for chat": a smaller copy of a capture for pasting into
/// AI chats and messaging apps, which downscale (or reject) big images anyway.
/// </summary>
public static class ChatImage
{
    /// <summary>The longest side, in pixels. Larger captures are scaled down to it; smaller ones are left alone.</summary>
    public const int MaxEdge = 1568;

    /// <summary>A PNG bigger than this (1 MB, counted as File Explorer does: 1024 × 1024) is copied as a JPEG instead.</summary>
    public const long MaxPngBytes = 1024 * 1024;

    /// <summary>JPEG quality, in percent, used when the PNG is too big.</summary>
    public const int JpegQuality = 85;

    /// <summary>The size to copy at: the long edge at most <paramref name="maxEdge"/>, never scaled up, aspect ratio kept.</summary>
    public static (int Width, int Height) ScaledSize(int width, int height, int maxEdge = MaxEdge)
    {
        var longEdge = Math.Max(width, height);
        if (longEdge <= maxEdge) return (width, height);
        var scale = (double)maxEdge / longEdge;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>Whether a PNG of this many bytes is too big to copy as it is.</summary>
    public static bool UseJpeg(long pngBytes) => pngBytes > MaxPngBytes;

    /// <summary>Kilobytes as File Explorer shows them (1 KB = 1024 bytes), at least 1.</summary>
    public static string FormatSize(long bytes) => $"{Math.Max(1, (bytes + 512) / 1024)} KB";

    public static string Describe(int width, int height, long bytes) =>
        $"Copied for chat · {width} × {height} · {FormatSize(bytes)}";

    /// <summary>
    /// Scales premultiplied BGRA pixels down by averaging the source pixels under each new pixel (a box
    /// filter, which is what scaling down needs). Returns <paramref name="bgra"/> itself when the size
    /// doesn't change.
    /// </summary>
    public static byte[] Downscale(byte[] bgra, int width, int height, int newWidth, int newHeight)
    {
        if (newWidth > width || newHeight > height)
            throw new ArgumentException("Copy for chat never scales up.");
        if (bgra.Length != width * height * 4)
            throw new ArgumentException("Pixel buffer does not match dimensions.", nameof(bgra));
        if (newWidth == width && newHeight == height) return bgra;

        var output = new byte[newWidth * newHeight * 4];
        Parallel.For(0, newHeight, y =>
        {
            int y0 = (int)((long)y * height / newHeight);
            int y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * height / newHeight));
            for (var x = 0; x < newWidth; x++)
            {
                int x0 = (int)((long)x * width / newWidth);
                int x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * width / newWidth));
                long b = 0, g = 0, r = 0, a = 0;
                for (var sy = y0; sy < y1; sy++)
                for (var sx = x0; sx < x1; sx++)
                {
                    var i = (sy * width + sx) * 4;
                    b += bgra[i];
                    g += bgra[i + 1];
                    r += bgra[i + 2];
                    a += bgra[i + 3];
                }
                var n = (long)(y1 - y0) * (x1 - x0);
                var o = (y * newWidth + x) * 4;
                output[o] = (byte)((b + n / 2) / n);
                output[o + 1] = (byte)((g + n / 2) / n);
                output[o + 2] = (byte)((r + n / 2) / n);
                output[o + 3] = (byte)((a + n / 2) / n);
            }
        });
        return output;
    }

    /// <summary>
    /// Premultiplied BGRA composited onto white, fully opaque: JPEG has no alpha, so transparent
    /// areas (freeform shapes, window corners) become white rather than black.
    /// </summary>
    public static byte[] FlattenOnWhite(byte[] bgra)
    {
        var output = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var behind = 255 - bgra[i + 3]; // how much white shows through
            output[i] = (byte)Math.Min(255, bgra[i] + behind);
            output[i + 1] = (byte)Math.Min(255, bgra[i + 1] + behind);
            output[i + 2] = (byte)Math.Min(255, bgra[i + 2] + behind);
            output[i + 3] = 255;
        }
        return output;
    }
}
