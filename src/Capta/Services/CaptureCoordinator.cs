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
        // Freeze every monitor first; the overlay draws on top of these frames.
        var frames = await Task.WhenAll(monitors.Select(_source.CaptureMonitorAsync));

        return await OverlaySession.RunAsync(mode, monitors, frames) switch
        {
            OverlayResult.RegionSelected r => r.Frame.Crop(r.Rect),
            // Live capture: gets the whole window even where it was occluded or off-monitor.
            OverlayResult.WindowSelected w => await _source.CaptureWindowAsync(w.Target.Handle),
            _ => null,
        };
    }
}
