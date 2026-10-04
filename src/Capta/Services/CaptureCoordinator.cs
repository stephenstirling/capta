using Capta.Capture;
using Capta.Interop;
using Capta.Overlay;
using Windows.Graphics;

namespace Capta.Services;

/// <summary>Runs one capture at a time: freeze → select (overlay) → produce an image.</summary>
public sealed class CaptureCoordinator
{
    private readonly IScreenSource _source;
    private bool _busy;

    public CaptureCoordinator(IScreenSource source) => _source = source;

    /// <summary>Raised on the UI thread with each finished capture.</summary>
    public event Action<CaptureResult>? Captured;

    public async Task RunAsync(CaptureMode mode)
    {
        if (_busy)
        {
            Log.Info("Capture ignored: one is already running");
            return;
        }
        _busy = true;
        try
        {
            var result = mode switch
            {
                CaptureMode.FullScreen => await CaptureFullScreenAsync(),
                CaptureMode.ActiveWindow => await CaptureActiveWindowAsync(),
                _ => await SelectAsync(mode),
            };
            if (result is not null)
                Captured?.Invoke(result);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<CaptureResult> CaptureFullScreenAsync()
    {
        var monitor = Monitors.AtPoint(WindowHelper.CursorPosition());
        return new CaptureResult(await _source.CaptureMonitorAsync(monitor), monitor, monitor.Bounds);
    }

    /// <summary>The foreground window, captured live; falls back to full screen over the desktop or shell.</summary>
    private async Task<CaptureResult> CaptureActiveWindowAsync()
    {
        var target = WindowFinder.Foreground();
        if (target is null)
            return await CaptureFullScreenAsync();

        var image = await _source.CaptureWindowAsync(target.Handle);
        var b = target.Bounds;
        var monitor = Monitors.AtPoint(new PointInt32(b.X + b.Width / 2, b.Y + b.Height / 2));
        return new CaptureResult(image, monitor, new RectInt32(b.X, b.Y, image.Width, image.Height));
    }

    private async Task<CaptureResult?> SelectAsync(CaptureMode mode)
    {
        var monitors = Monitors.All();
        // Freeze every monitor first; the overlay draws on top of these frames.
        var frames = await Task.WhenAll(monitors.Select(_source.CaptureMonitorAsync));

        Log.Info($"Froze {frames.Length} monitor(s); showing overlay");
        var selection = await OverlaySession.RunAsync(mode, monitors, frames);
        Log.Info($"Overlay result: {selection.GetType().Name}");
        switch (selection)
        {
            case OverlayResult.RegionSelected r:
            {
                var b = r.Monitor.Bounds;
                var screen = new RectInt32(b.X + r.Rect.X, b.Y + r.Rect.Y, r.Rect.Width, r.Rect.Height);
                return new CaptureResult(r.Frame.Crop(r.Rect), r.Monitor, screen);
            }
            case OverlayResult.WindowSelected w:
            {
                // Live capture: gets the whole window even where it was occluded or off-monitor.
                var image = await _source.CaptureWindowAsync(w.Target.Handle);
                var b = w.Target.Bounds;
                var monitor = Monitors.AtPoint(new PointInt32(b.X + b.Width / 2, b.Y + b.Height / 2));
                return new CaptureResult(image, monitor, new RectInt32(b.X, b.Y, image.Width, image.Height));
            }
            default:
                return null;
        }
    }
}
