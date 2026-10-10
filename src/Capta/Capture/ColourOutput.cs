using Capta.Services;

namespace Capta.Capture;

/// <summary>
/// Turns a capture's pixels into what leaves Capta (clipboard, saved files, Ocula): corrected for
/// the source display's profile when that's switched on, and in sRGB or Display P3.
/// </summary>
public static class ColourOutput
{
    private static readonly Lazy<byte[]> s_displayP3 = new(LoadDisplayP3);

    /// <summary>The Display P3 ICC profile (Capture/DisplayP3.icc, from tools/generate_display_p3_icc.py).</summary>
    public static byte[] DisplayP3Profile => s_displayP3.Value;

    /// <summary>
    /// The capture's pixels in <paramref name="target"/>, and the space they're actually in: if a
    /// conversion fails, the pixels as captured are returned as sRGB rather than failing the save.
    /// </summary>
    public static Task<(byte[] Pixels, ColourSpace Space)> ConvertAsync(CapturedImage image, ColourSpace target) =>
        Task.Run(() => Convert(image, target));

    private static (byte[] Pixels, ColourSpace Space) Convert(CapturedImage image, ColourSpace target)
    {
        try
        {
            if (image.HdrPixels is { } hdr)
            {
                // HDR displays are colour-managed by Windows. For P3, tone-map the original again
                // rather than converting the sRGB result, which has lost what sRGB can't hold.
                if (target == ColourSpace.Srgb) return (image.Pixels, ColourSpace.Srgb);
                var map = image.ToneMap ?? new HdrToneMap(80f, HighlightRollOff.Clip, null);
                var p3 = ToneMapper.ToSdr(hdr, image.Width, image.Height, map.SdrWhiteNits, map.RollOff, target, map.Peaks);
                return (KeepAlpha(p3.Pixels, image.Pixels), target);
            }

            var to = target == ColourSpace.DisplayP3 ? DisplayP3Profile : null;
            if (image.SourceProfile is null && to is null) return (image.Pixels, ColourSpace.Srgb);
            var converted = WicColour.Convert(image.Pixels, image.Width, image.Height, image.SourceProfile, to);
            return (KeepAlpha(converted, image.Pixels), target);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't convert the capture to {target}; using it as captured", ex);
            return (image.Pixels, ColourSpace.Srgb);
        }
    }

    /// <summary>
    /// Copies alpha from the original. Captures are fully opaque or fully transparent (freeform),
    /// so premultiplication only means clearing the transparent pixels.
    /// </summary>
    internal static byte[] KeepAlpha(byte[] converted, byte[] original)
    {
        for (var i = 3; i < converted.Length; i += 4)
        {
            var alpha = original[i];
            converted[i] = alpha;
            if (alpha == 0)
                converted[i - 3] = converted[i - 2] = converted[i - 1] = 0;
        }
        return converted;
    }

    private static byte[] LoadDisplayP3()
    {
        using var stream = typeof(ColourOutput).Assembly.GetManifestResourceStream("DisplayP3.icc")
            ?? throw new InvalidOperationException("The Display P3 profile isn't embedded.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
