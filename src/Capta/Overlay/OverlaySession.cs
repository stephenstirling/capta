using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace Capta.Overlay;

public abstract record OverlayResult
{
    public sealed record Cancelled : OverlayResult;

    /// <param name="Rect">Selection in physical pixels, relative to <paramref name="Monitor"/>.</param>
    /// <param name="Mode">Region, or FullScreen when the mode bar's Full screen was used.</param>
    public sealed record RegionSelected(MonitorInfo Monitor, CapturedImage Frame, RectInt32 Rect, CaptureMode Mode) : OverlayResult;

    public sealed record WindowSelected(WindowTarget Target) : OverlayResult;

    /// <param name="Points">The drawn outline in physical pixels, relative to <paramref name="Monitor"/>.</param>
    public sealed record FreeformSelected(MonitorInfo Monitor, CapturedImage Frame, PointInt32[] Points) : OverlayResult;

    /// <summary>Close, wait this long, then freeze the screen and show the overlay again.</summary>
    public sealed record Delayed(int Seconds, CaptureMode Mode) : OverlayResult;

    /// <summary>C was pressed: the colour under the cursor, and where it is on the virtual desktop.</summary>
    public sealed record ColourPicked(Windows.UI.Color Colour, PointInt32 ScreenPoint) : OverlayResult;

    /// <summary>A settings window was chosen from Capture settings; the capture is abandoned.</summary>
    public sealed record OpenSettings(SettingsPage Page) : OverlayResult;
}

/// <summary>The settings windows Capture settings can open.</summary>
public enum SettingsPage
{
    StartupAndShortcuts,
    ColourAndHdr,
}

/// <summary>Shows one overlay per monitor and resolves when any of them produces a result.</summary>
public sealed class OverlaySession
{
    private readonly TaskCompletionSource<OverlayResult> _result = new();
    private readonly List<OverlayWindow> _windows = [];
    private OverlayWindow? _pointerOwner;

    private static OverlaySession? s_active;

    /// <summary>True while an overlay is on screen.</summary>
    public static bool IsActive => s_active is not null;

    public static async Task<OverlayResult> RunAsync(CaptureMode mode, IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<CapturedImage> frames)
    {
        var session = new OverlaySession();
        s_active = session;
        // Snapshot windows up front (before our overlays exist) so Window mode can be chosen later.
        var targets = WindowFinder.Snapshot();
        var cursor = WindowHelper.CursorPosition();

        for (var i = 0; i < monitors.Count; i++)
            session._windows.Add(new OverlayWindow(session, monitors[i], frames[i], mode, targets));

        // Decode every backdrop before showing anything, so no monitor flashes black.
        await Task.WhenAll(session._windows.Select(w => w.PrepareAsync()));

        OverlayWindow? focused = null;
        for (var i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Contains(cursor)) focused = session._windows[i];
            else session._windows[i].Show(focus: false);
        }
        (focused ?? session._windows[0]).Show(focus: true);

        try
        {
            return await session._result.Task;
        }
        finally
        {
            s_active = null;
            foreach (var w in session._windows) w.Close();
        }
    }

    /// <summary>
    /// Esc for when the overlay doesn't have keyboard focus (Windows can refuse to move it to a window opened
    /// from a shortcut or another app's input), so the key never reaches the overlay's own handler: the first Esc
    /// clears a selection, the next cancels. False if no overlay needs it, or it already has the focus.
    /// </summary>
    public static bool HandleEscapeWithoutFocus()
    {
        if (s_active is not { } session) return false;
        var foreground = NativeMethods.GetForegroundWindow();
        if (session._windows.Any(w => w.GetHwnd() == foreground)) return false; // its own key handler gets Esc
        // Posted, not run here: this is called from the keyboard hook, which must return at once.
        DispatcherQueue.GetForCurrentThread().TryEnqueue(session.EscapePressed);
        return true;
    }

    private void EscapePressed()
    {
        if (_windows.Any(w => w.TryClearSelectionForEscape())) return;
        Cancel();
    }

    internal void Complete(OverlayResult result) => _result.TrySetResult(result);

    /// <summary>Switches every monitor's overlay between Region and Window.</summary>
    internal void SetMode(CaptureMode mode)
    {
        foreach (var w in _windows) w.ApplyMode(mode);
    }

    internal void Cancel() => _result.TrySetResult(new OverlayResult.Cancelled());

    internal void NotifyPointerOn(OverlayWindow window)
    {
        if (_pointerOwner == window) return;
        _pointerOwner?.OnPointerLeftMonitor();
        _pointerOwner = window;
    }
}
