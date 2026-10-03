using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Capta.Interop;

/// <summary>Small helpers for sizing and placing WinUI windows in DIPs.</summary>
internal static partial class WindowHelper
{
    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    private struct POINT { public int X, Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    public static nint GetHwnd(this Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static double GetScale(this Window window) => GetDpiForWindow(window.GetHwnd()) / 96.0;

    public static PointInt32 CursorPosition() => GetCursorPos(out var p) ? new PointInt32(p.X, p.Y) : default;

    /// <summary>The work area (physical pixels) of the monitor under the cursor.</summary>
    public static RectInt32 CursorWorkArea() =>
        DisplayArea.GetFromPoint(CursorPosition(), DisplayAreaFallback.Primary).WorkArea;

    public static void ResizeDip(this Window window, double width, double height)
    {
        var scale = window.GetScale();
        window.AppWindow.Resize(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    public static void CenterOnCursorMonitor(this Window window)
    {
        var area = CursorWorkArea();
        var size = window.AppWindow.Size;
        window.AppWindow.Move(new PointInt32(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (area.Height - size.Height) / 2));
    }

    public static void BringToFront(this Window window)
    {
        window.Activate();
        SetForegroundWindow(window.GetHwnd());
    }

    /// <summary>Configures a compact, non-resizable dialog-style window.</summary>
    public static void MakeToolWindow(this Window window, bool alwaysOnTop)
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = alwaysOnTop;
        window.AppWindow.SetPresenter(presenter);
        window.AppWindow.SetIcon("Assets/Capta.ico");
    }
}
