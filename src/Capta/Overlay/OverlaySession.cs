using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Windows.Graphics;

namespace Capta.Overlay;

public abstract record OverlayResult
{
    public sealed record Cancelled : OverlayResult;

    /// <param name="Rect">Selection in physical pixels, relative to <paramref name="Monitor"/>.</param>
    /// <param name="Mode">Region, or FullScreen when the mode bar's Full screen was used.</param>
    public sealed record RegionSelected(MonitorInfo Monitor, CapturedImage Frame, RectInt32 Rect, CaptureMode Mode) : OverlayResult;

    public sealed record WindowSelected(WindowTarget Target) : OverlayResult;
}

/// <summary>Shows one overlay per monitor and resolves when any of them produces a result.</summary>
public sealed class OverlaySession
{
    private readonly TaskCompletionSource<OverlayResult> _result = new();
    private readonly List<OverlayWindow> _windows = [];
    private OverlayWindow? _pointerOwner;

    public static async Task<OverlayResult> RunAsync(CaptureMode mode, IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<CapturedImage> frames)
    {
        var session = new OverlaySession();
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
            foreach (var w in session._windows) w.Close();
        }
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
