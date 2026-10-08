using Windows.UI;

namespace Stirling.Shared;

/// <summary>
/// Brand colours for code paths that draw outside XAML (overlays, bitmaps).
/// Mirrors tokens in Themes/Colors.xaml; prefer the XAML brushes wherever possible.
/// </summary>
public static class StirlingColors
{
    /// <summary>CaptaAccentColor.</summary>
    public static readonly Color Accent = Color.FromArgb(0xFF, 0xFF, 0xB5, 0x47);

    /// <summary>CaptaAccentTextColor in the light theme (amber is too light for text on white).</summary>
    public static readonly Color AccentTextLight = Color.FromArgb(0xFF, 0x8C, 0x64, 0x27);

    /// <summary>CaptaOnAccentColor: text and icons drawn on the accent.</summary>
    public static readonly Color OnAccent = Color.FromArgb(0xFF, 0x1A, 0x12, 0x06);

    /// <summary>
    /// Drop-shadow colour. The mockups use black, but pure black is see-through in the
    /// "sheet of glass" transparent windows, so this is a near-black that survives.
    /// </summary>
    public static readonly Color Shadow = Color.FromArgb(0xFF, 0x1A, 0x1B, 0x1F);

    /// <summary>CaptaScrimColor: capture-overlay dim, rgba(5,6,8,0.62).</summary>
    public static readonly Color Scrim = Color.FromArgb(0x9E, 0x05, 0x06, 0x08);

    /// <summary>Pack URI for merging the theme into an app's resources.</summary>
    public const string ThemeUri = "ms-appx:///Stirling.Shared/Themes/Colors.xaml";
}
