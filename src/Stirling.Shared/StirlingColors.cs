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

    /// <summary>Pack URI for merging the theme into an app's resources.</summary>
    public const string ThemeUri = "ms-appx:///Stirling.Shared/Themes/Colors.xaml";
}
