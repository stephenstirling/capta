using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Capta.Interop.NativeMethods;

namespace Capta.Interop;

/// <summary>
/// Lets clicks on a floating window's transparent shadow margin reach the windows underneath.
/// </summary>
/// <remarks>
/// HTTRANSPARENT only forwards clicks to windows on the same thread, so this toggles
/// WS_EX_TRANSPARENT instead: on while the cursor is outside the panel, off while it's over it.
/// A transparent window gets no mouse input, so the cursor is polled (only while visible).
/// </remarks>
internal static partial class ClickThroughMargins
{
    private const int PollMs = 30;
    private const int VK_LBUTTON = 0x01;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hwnd, ref POINT point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    private struct POINT { public int X, Y; }

    /// <param name="panel">The visible card; everything outside it passes clicks through.</param>
    public static void Attach(Window window, FrameworkElement panel)
    {
        var hwnd = window.GetHwnd();
        var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

        var passThrough = false;
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(PollMs);
        timer.Tick += (_, _) =>
        {
            if (!window.AppWindow.IsVisible || panel.XamlRoot is null) return;
            // Mid-drag (moving the window) and open menus keep the window as it is.
            if (IsKeyDown(VK_LBUTTON) && !passThrough) return;
            if (!passThrough && VisualTreeHelper.GetOpenPopupsForXamlRoot(panel.XamlRoot).Count > 0) return;

            var over = IsOverPanel(hwnd, panel);
            if (over == !passThrough) return;
            passThrough = !over;
            var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            ex = passThrough ? ex | WS_EX_TRANSPARENT : ex & ~(long)WS_EX_TRANSPARENT;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
        };
        timer.Start();
        window.Closed += (_, _) => timer.Stop();
    }

    private static bool IsOverPanel(nint hwnd, FrameworkElement panel)
    {
        if (!GetCursorPos(out var cursor)) return true;
        var origin = new POINT();
        ClientToScreen(hwnd, ref origin);
        var scale = panel.XamlRoot.RasterizationScale;
        var bounds = panel.TransformToVisual(null).TransformBounds(
            new Windows.Foundation.Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
        var x = (cursor.X - origin.X) / scale;
        var y = (cursor.Y - origin.Y) / scale;
        return x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
    }
}
