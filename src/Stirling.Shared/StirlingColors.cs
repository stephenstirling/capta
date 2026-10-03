using Windows.UI;

namespace Stirling.Shared;

/// <summary>Brand colours for code paths that draw outside XAML (overlays, bitmaps).</summary>
public static class StirlingColors
{
    public static readonly Color Amber = Color.FromArgb(0xFF, 0xFF, 0xB5, 0x47);
    public static readonly Color AmberDark = Color.FromArgb(0xFF, 0xB0, 0x74, 0x1A);
    public static readonly Color Ink = Color.FromArgb(0xFF, 0x1E, 0x1A, 0x14);

    /// <summary>Pack URI for merging the theme into an app's resources.</summary>
    public const string ThemeUri = "ms-appx:///Stirling.Shared/Themes/Colors.xaml";
}
