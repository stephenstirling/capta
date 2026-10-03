using Capta.Capture;
using Capta.Interop;
using Capta.Overlay;

namespace Capta.Services;

/// <summary>Runs one capture at a time: freeze → select (overlay) → produce an image.</summary>
public sealed class CaptureCoordinator
{
    private readonly IScreenSource _source;
    private bool _busy;

    public CaptureCoordinator(IScreenSource source) => _source = source;

    /// <summary>Raised on the UI thread with each finished capture.</summary>
    public event Action<CapturedImage>? Captured;

    public async Task RunAsync(CaptureMode mode)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var image = mode == CaptureMode.FullScreen
                ? await _source.CaptureMonitorAsync(Monitors.AtPoint(WindowHelper.CursorPosition()))
                : await SelectAsync(mode);
            if (image is not null)
                Captured?.Invoke(image);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<CapturedImage?> SelectAsync(CaptureMode mode)
    {
        var monitors = Monitors.All();
        var frames = await Task.WhenAll(monitors.Select(_source.CaptureMonitorAsync));

        return await OverlaySession.RunAsync(mode, monitors, frames) switch
        {
            OverlayResult.RegionSelected r => r.Frame.Crop(r.Rect),
            OverlayResult.WindowSelected w => await CaptureWindowAsync(w.Target, monitors, frames),
            _ => null,
        };
    }

    private Task<CapturedImage> CaptureWindowAsync(WindowTarget target, IReadOnlyList<MonitorInfo> monitors, CapturedImage[] frames)
    {
        // Interim: crop the window's frame out of the frozen monitor image.
        var cx = target.Bounds.X + target.Bounds.Width / 2;
        var cy = target.Bounds.Y + target.Bounds.Height / 2;
        var i = Math.Max(0, monitors.ToList().FindIndex(m => m.Contains(new(cx, cy))));
        var b = monitors[i].Bounds;
        return Task.FromResult(frames[i].Crop(new(target.Bounds.X - b.X, target.Bounds.Y - b.Y, target.Bounds.Width, target.Bounds.Height)));
    }
}
