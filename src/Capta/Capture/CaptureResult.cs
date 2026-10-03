using Windows.Graphics;

namespace Capta.Capture;

/// <param name="Image">The SDR capture.</param>
/// <param name="Monitor">Monitor the capture came from (where the card appears).</param>
/// <param name="ScreenBounds">Where the captured pixels were on the virtual desktop (where a pin appears).</param>
public sealed record CaptureResult(CapturedImage Image, MonitorInfo Monitor, RectInt32 ScreenBounds);
