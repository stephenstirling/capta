using Microsoft.UI.Xaml;
using static Capta.Interop.NativeMethods;

namespace Capta.Interop;

/// <summary>Keeps Capta's own floating UI (toolbar, card) out of every screen capture.</summary>
internal static class CaptureExclusion
{
    public static void Apply(Window window)
    {
#if DEBUG
        // Debug builds only: %USERPROFILE%\.capta-debug\show-in-captures keeps floating UI
        // capturable so it can be screenshotted and compared with design/screenshots.
        var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".capta-debug", "show-in-captures");
        if (File.Exists(marker)) return;
#endif
        SetWindowDisplayAffinity(window.GetHwnd(), WDA_EXCLUDEFROMCAPTURE);
    }
}
