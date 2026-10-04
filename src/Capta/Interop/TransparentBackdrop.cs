using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Capta.Interop.NativeMethods;

namespace Capta.Interop;

/// <summary>
/// A fully transparent system backdrop, so a borderless window can draw its own
/// rounded panel and shadow (the floating toolbar, card and pins use radii and
/// shadows DWM can't produce).
/// </summary>
public sealed partial class TransparentBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? s_compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        connectedTarget.SystemBackdrop = Compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        disconnectedTarget.SystemBackdrop = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    /// <summary>Strips DWM's own border and corner rounding from a window that uses this backdrop.</summary>
    public static void PrepareWindow(Window window)
    {
        var hwnd = window.GetHwnd();
        SetDwmInt(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);
        SetDwmInt(hwnd, DWMWA_BORDER_COLOR, unchecked((int)DWMWA_COLOR_NONE));
        // "Sheet of glass": lets DWM honour the backdrop's alpha instead of drawing black.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, in margins);
        window.SystemBackdrop = new TransparentBackdrop();
    }

    // The backdrop brush must come from a system (Windows.UI) compositor, which needs a
    // Windows.System.DispatcherQueue on this thread; WinUI only creates the Microsoft.UI one.
    private static Windows.UI.Composition.Compositor Compositor
    {
        get
        {
            if (s_compositor is null)
            {
                if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
                {
                    var options = new DispatcherQueueOptions
                    {
                        dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                        threadType = 2,    // DQTYPE_THREAD_CURRENT
                        apartmentType = 0, // DQTAT_COM_NONE
                    };
                    Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out _));
                }
                s_compositor = new Windows.UI.Composition.Compositor();
            }
            return s_compositor;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, in MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
