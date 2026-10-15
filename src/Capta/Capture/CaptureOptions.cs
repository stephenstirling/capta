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

/// <summary>What to keep when the screen being captured is in HDR (Colour &amp; HDR window).</summary>
public enum HdrHandling
{
    /// <summary>Tone-map to SDR; JPEG XR is still offered by Save As and the Copy menu.</summary>
    ToneMap,
    /// <summary>Save As defaults to JPEG XR (full range); the clipboard still gets SDR.</summary>
    KeepHdr,
    /// <summary>Save As writes the SDR PNG plus a .jxr HDR original beside it.</summary>
    SaveBoth,
}

/// <summary>The colour space saved files are written in (Colour &amp; HDR → Colour output).</summary>
public enum ColourSpace
{
    /// <summary>What every app assumes for untagged images. Always used for the clipboard.</summary>
    Srgb,
    /// <summary>Wider gamut (DCI-P3 primaries, D65, sRGB curve); files carry the profile so apps show them right.</summary>
    DisplayP3,
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

    /// <summary>Tag saved PNGs as sRGB. Display P3 files always carry their profile.</summary>
    public bool EmbedColourProfile { get; init; } = true;

    public ColourSpace ColourSpace { get; init; } = ColourSpace.Srgb;

    /// <summary>
    /// Treat captures of an SDR display as being in that display's ICC profile and convert them on
    /// the way out, so they look as they did on screen. HDR and Auto Color Management displays are
    /// already colour-managed by Windows and are left alone.
    /// </summary>
    public bool CorrectMonitorProfiles { get; init; }
}
