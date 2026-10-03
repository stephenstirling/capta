namespace Capta.Capture;

/// <summary>Produces SDR images of monitors and windows.</summary>
public interface IScreenSource
{
    Task<CapturedImage> CaptureMonitorAsync(MonitorInfo monitor);

    Task<CapturedImage> CaptureWindowAsync(nint hwnd);
}
