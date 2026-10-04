using System.Runtime.InteropServices;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Overlay;

/// <summary>A capturable top-level window and its visible frame, in physical pixels.</summary>
public sealed record WindowTarget(nint Handle, RectInt32 Bounds);

/// <summary>
/// Snapshots the top-level windows in Z-order when the overlay opens, so window
/// mode can hit-test the frozen desktop without seeing Capta's own overlays.
/// </summary>
public static class WindowFinder
{
    private static readonly HashSet<string> s_ignoredClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    [ThreadStatic] private static List<nint>? t_collected;

    public static unsafe IReadOnlyList<WindowTarget> Snapshot()
    {
        t_collected = [];
        EnumWindows(&Collect, 0);
        var ownPid = (uint)Environment.ProcessId;
        var result = new List<WindowTarget>();
        foreach (var hwnd in t_collected)
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) continue;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == ownPid) continue;

            var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT)) != 0) continue;

            int cloaked = 0;
            DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int));
            if (cloaked != 0) continue;

            if (s_ignoredClasses.Contains(GetClassName(hwnd))) continue;

            RECT r;
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT)) != 0) continue;
            if (r.Width < 8 || r.Height < 8) continue;

            result.Add(new WindowTarget(hwnd, new RectInt32(r.Left, r.Top, r.Width, r.Height)));
        }
        t_collected = null;
        return result;
    }

    /// <summary>The foreground top-level window, unless it's Capta's own, the desktop or the taskbar.</summary>
    public static unsafe WindowTarget? Foreground()
    {
        var hwnd = GetAncestor(GetForegroundWindow(), GA_ROOT);
        if (hwnd == 0 || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId || s_ignoredClasses.Contains(GetClassName(hwnd))) return null;

        RECT r;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT)) != 0) return null;
        return new WindowTarget(hwnd, new RectInt32(r.Left, r.Top, r.Width, r.Height));
    }

    /// <summary>Topmost window whose frame contains <paramref name="p"/>.</summary>
    public static WindowTarget? HitTest(IReadOnlyList<WindowTarget> windows, PointInt32 p)
    {
        foreach (var w in windows)
        {
            var b = w.Bounds;
            if (p.X >= b.X && p.Y >= b.Y && p.X < b.X + b.Width && p.Y < b.Y + b.Height)
                return w;
        }
        return null;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint _)
    {
        t_collected!.Add(hwnd);
        return 1;
    }
}
