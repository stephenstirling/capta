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

    /// <summary>An area was chosen to record (Record mode): the monitor and the area on the desktop.</summary>
    public event Action<MonitorInfo, RectInt32>? RecordRequested;

    /// <summary>A colour was picked in the overlay (C) instead of a capture.</summary>
    public event Action<Windows.UI.Color, PointInt32>? ColourPicked;

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
        return new CaptureResult(await _source.CaptureMonitorAsync(monitor), monitor, monitor.Bounds,
            CaptureMode.FullScreen, GraphicsCaptureSource.IsHdr(monitor.Handle));
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
        var (app, title) = WindowFinder.Describe(target.Handle);
        return new CaptureResult(image, monitor, new RectInt32(b.X, b.Y, image.Width, image.Height),
            CaptureMode.ActiveWindow, GraphicsCaptureSource.IsHdr(monitor.Handle))
        {
            SourceApp = app,
            WindowTitle = title,
        };
    }

    private async Task<CaptureResult?> SelectAsync(CaptureMode mode)
    {
        IReadOnlyList<MonitorInfo> monitors;
        CapturedImage[] frames;
        OverlayResult selection;
        while (true)
        {
            monitors = Monitors.All();
            // Freeze every monitor first; the overlay draws on top of these frames.
            frames = await Task.WhenAll(monitors.Select(_source.CaptureMonitorAsync));

            Log.Info($"Froze {frames.Length} monitor(s); showing overlay");
            selection = await OverlaySession.RunAsync(mode, monitors, frames);
            Log.Info($"Overlay result: {selection.GetType().Name}");
            if (selection is not OverlayResult.Delayed delayed)
                break;

            // The overlay is gone; give the user time to set the screen up, then freeze again.
            mode = delayed.Mode;
            await Task.Delay(TimeSpan.FromSeconds(delayed.Seconds));
        }
        switch (selection)
        {
            case OverlayResult.RegionSelected r when r.Mode == CaptureMode.Recording:
            {
                var b = r.Monitor.Bounds;
                RecordRequested?.Invoke(r.Monitor, new RectInt32(b.X + r.Rect.X, b.Y + r.Rect.Y, r.Rect.Width, r.Rect.Height));
                return null;
            }
            case OverlayResult.RegionSelected r when r.Mode == CaptureMode.Scrolling:
            {
                var b = r.Monitor.Bounds;
                var screen = new RectInt32(b.X + r.Rect.X, b.Y + r.Rect.Y, r.Rect.Width, r.Rect.Height);
                var under = WindowFinder.At(new PointInt32(screen.X + screen.Width / 2, screen.Y + screen.Height / 2));
                var (app, title) = under is null ? (null, null) : WindowFinder.Describe(under.Handle);
                var image = await ScrollingCapture.RunAsync(_source, r.Monitor, screen);
                return new CaptureResult(image, r.Monitor, new RectInt32(screen.X, screen.Y, image.Width, image.Height),
                    CaptureMode.Scrolling, GraphicsCaptureSource.IsHdr(r.Monitor.Handle))
                {
                    SourceApp = app,
                    WindowTitle = title,
                };
            }
            case OverlayResult.RegionSelected r:
            {
                var b = r.Monitor.Bounds;
                var screen = new RectInt32(b.X + r.Rect.X, b.Y + r.Rect.Y, r.Rect.Width, r.Rect.Height);
                // Attribute the region to the window under its centre (the overlay has closed).
                var under = WindowFinder.At(new PointInt32(screen.X + screen.Width / 2, screen.Y + screen.Height / 2));
                var (app, title) = under is null ? (null, null) : WindowFinder.Describe(under.Handle);
                return new CaptureResult(r.Frame.Crop(r.Rect), r.Monitor, screen,
                    r.Mode, GraphicsCaptureSource.IsHdr(r.Monitor.Handle))
                {
                    SourceApp = app,
                    WindowTitle = title,
                };
            }
            case OverlayResult.FreeformSelected f:
            {
                int x0 = f.Points.Min(p => p.X), y0 = f.Points.Min(p => p.Y);
                int x1 = f.Points.Max(p => p.X), y1 = f.Points.Max(p => p.Y);
                var rect = new RectInt32(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
                var local = f.Points.Select(p => new PointInt32(p.X - x0, p.Y - y0)).ToArray();
                var image = f.Frame.Crop(rect).MaskOutside(local);
                var b = f.Monitor.Bounds;
                var screen = new RectInt32(b.X + rect.X, b.Y + rect.Y, rect.Width, rect.Height);
                var under = WindowFinder.At(new PointInt32(screen.X + screen.Width / 2, screen.Y + screen.Height / 2));
                var (app, title) = under is null ? (null, null) : WindowFinder.Describe(under.Handle);
                return new CaptureResult(image, f.Monitor, screen, CaptureMode.Freeform, GraphicsCaptureSource.IsHdr(f.Monitor.Handle))
                {
                    SourceApp = app,
                    WindowTitle = title,
                };
            }
            case OverlayResult.ColourPicked c:
                ColourPicked?.Invoke(c.Colour, c.ScreenPoint);
                return null;
            case OverlayResult.WindowSelected w:
            {
                // Live capture: gets the whole window even where it was occluded or off-monitor.
                var image = await _source.CaptureWindowAsync(w.Target.Handle);
                var b = w.Target.Bounds;
                var monitor = Monitors.AtPoint(new PointInt32(b.X + b.Width / 2, b.Y + b.Height / 2));
                var (app, title) = WindowFinder.Describe(w.Target.Handle);
                return new CaptureResult(image, monitor, new RectInt32(b.X, b.Y, image.Width, image.Height),
                    mode, GraphicsCaptureSource.IsHdr(monitor.Handle))
                {
                    SourceApp = app,
                    WindowTitle = title,
                };
            }
            default:
                return null;
        }
    }
}
