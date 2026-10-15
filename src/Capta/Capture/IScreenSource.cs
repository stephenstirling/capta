namespace Capta.Capture;

/// <summary>Produces SDR images of monitors and windows.</summary>
public interface IScreenSource
{
    Task<CapturedImage> CaptureMonitorAsync(MonitorInfo monitor);

    /// <summary>A live capture of a monitor, drawing the mouse cursor in it when asked. (The frozen frames behind the overlay never have it.)</summary>
    Task<CapturedImage> CaptureMonitorAsync(MonitorInfo monitor, bool includeCursor);

    Task<CapturedImage> CaptureWindowAsync(nint hwnd);
}
