namespace Capta.Capture;

public enum HighlightRollOff
{
    /// <summary>Exact SDR; anything brighter than SDR white is clipped hue-preservingly.</summary>
    Clip,
    /// <summary>Compress highlights above 80% of SDR white.</summary>
    Balanced,
    /// <summary>Compress highlights above 60% of SDR white: gentler, keeps more highlight detail.</summary>
    Soft,
}

/// <summary>Colour settings the capture engine reads. The app keeps these in sync with Settings.</summary>
public sealed class CaptureOptions
{
    public static CaptureOptions Current { get; set; } = new();

    /// <summary>Use the SDR white level from Windows (its HDR brightness slider) for each display.</summary>
    public bool MatchWindowsSdrWhite { get; init; } = true;

    /// <summary>SDR white in nits when <see cref="MatchWindowsSdrWhite"/> is off.</summary>
    public float SdrWhiteNits { get; init; } = 240;

    public HighlightRollOff RollOff { get; init; } = HighlightRollOff.Clip;

    /// <summary>Tag saved PNGs as sRGB.</summary>
    public bool EmbedColourProfile { get; init; } = true;
}
