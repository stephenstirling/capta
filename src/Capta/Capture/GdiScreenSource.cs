using static Capta.Interop.NativeMethods;

namespace Capta.Capture;

/// <summary>
/// Interim SDR source using BitBlt from the desktop DC. Not HDR-aware; replaced by
/// Windows.Graphics.Capture in the capture step.
/// </summary>
public sealed class GdiScreenSource : IScreenSource
{
    public Task<CapturedImage> CaptureMonitorAsync(MonitorInfo monitor) =>
        Task.FromResult(Blt(monitor.Bounds.X, monitor.Bounds.Y, monitor.Bounds.Width, monitor.Bounds.Height));

    public unsafe Task<CapturedImage> CaptureWindowAsync(nint hwnd)
    {
        RECT r;
        DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &r, sizeof(RECT));
        return Task.FromResult(Blt(r.Left, r.Top, r.Width, r.Height));
    }

    private static unsafe CapturedImage Blt(int x, int y, int width, int height)
    {
        var screen = GetDC(0);
        var mem = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            biSize = sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = CreateDIBSection(screen, &header, 0, out var bits, 0, 0);
        var old = SelectObject(mem, dib);
        try
        {
            BitBlt(mem, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT);
            var pixels = new byte[width * height * 4];
            new ReadOnlySpan<byte>((void*)bits, pixels.Length).CopyTo(pixels);
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 0xFF;
            return new CapturedImage(width, height, pixels);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(dib);
            DeleteDC(mem);
            ReleaseDC(0, screen);
        }
    }
}
