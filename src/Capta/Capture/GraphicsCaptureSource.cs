using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using static Capta.Interop.NativeMethods;

namespace Capta.Capture;

/// <summary>
/// Captures monitors and windows with Windows.Graphics.Capture in FP16 (scRGB) and
/// tone-maps to SDR using each display's AdvancedColorInfo.SdrWhiteLevelInNits.
/// </summary>
public sealed class GraphicsCaptureSource : IScreenSource, IDisposable
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(2);

    private Direct3D? _d3d;
    private bool _borderlessRequested;

    public async Task<CapturedImage> CaptureMonitorAsync(MonitorInfo monitor)
    {
        var item = WinRTInterop.CreateItemForMonitor(monitor.Handle);
        var (pixels, w, h) = await GrabAsync(item);
        return await ToImageAsync(pixels, w, h, monitor.Handle);
    }

    public async Task<CapturedImage> CaptureWindowAsync(nint hwnd)
    {
        var item = WinRTInterop.CreateItemForWindow(hwnd);
        var (pixels, w, h) = await GrabAsync(item);
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        return await ToImageAsync(pixels, w, h, monitor);
    }

    /// <summary>
    /// The SDR image. From an HDR display it keeps the FP16 original (and how it was tone-mapped);
    /// from an SDR display it notes the display's profile when captures are corrected for it.
    /// </summary>
    private static async Task<CapturedImage> ToImageAsync(ushort[] fp16, int width, int height, nint hmonitor)
    {
        var options = CaptureOptions.Current;
        var white = SdrWhiteLevel(hmonitor);
        var sdr = ToneMapper.ToSdr(fp16, width, height, white, options.RollOff);
        if (IsHdr(hmonitor))
            return new CapturedImage(width, height, sdr.Pixels) { HdrPixels = fp16, SdrWhiteNits = white, RollOff = options.RollOff };
        if (options.CorrectMonitorProfiles && await MonitorProfileAsync(hmonitor) is { } profile)
            return new CapturedImage(width, height, sdr.Pixels) { SourceProfile = profile };
        return sdr;
    }

    /// <summary>
    /// The ICC profile of an SDR display that Windows doesn't colour-manage itself. Null for HDR and
    /// Auto Color Management displays, when no profile is set, or if it can't be read.
    /// </summary>
    public static async Task<byte[]?> MonitorProfileAsync(nint hmonitor)
    {
        try
        {
            var display = WinRTInterop.GetDisplayInformation(hmonitor);
            if (display.GetAdvancedColorInfo().CurrentAdvancedColorKind != Windows.Graphics.Display.AdvancedColorKind.StandardDynamicRange)
                return null;
            using var stream = await display.GetColorProfileAsync();
            if (stream is null || stream.Size == 0) return null;
            using var bytes = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(bytes);
            return bytes.ToArray();
        }
        catch (Exception ex)
        {
            Capta.Services.Log.Info($"Display colour profile unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// HDR state, whether an SDR display uses Auto Color Management, and the Windows SDR white level
    /// for a monitor (null if unavailable).
    /// </summary>
    public static (bool Hdr, bool AutoColour, float SdrWhiteNits)? DisplayColour(nint hmonitor)
    {
        try
        {
            var info = WinRTInterop.GetDisplayInformation(hmonitor).GetAdvancedColorInfo();
            return (info.CurrentAdvancedColorKind == Windows.Graphics.Display.AdvancedColorKind.HighDynamicRange,
                info.CurrentAdvancedColorKind == Windows.Graphics.Display.AdvancedColorKind.WideColorGamut,
                (float)info.SdrWhiteLevelInNits);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether the monitor is currently showing HDR (captures will be tone-mapped).</summary>
    public static bool IsHdr(nint hmonitor)
    {
        try
        {
            return WinRTInterop.GetDisplayInformation(hmonitor).GetAdvancedColorInfo().CurrentAdvancedColorKind
                == Windows.Graphics.Display.AdvancedColorKind.HighDynamicRange;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// SDR white in nits for the monitor: the Windows value (or the user's override) on an HDR
    /// display; 80 (the scRGB reference) when SDR or unknown.
    /// </summary>
    private static float SdrWhiteLevel(nint hmonitor)
    {
        try
        {
            var info = WinRTInterop.GetDisplayInformation(hmonitor).GetAdvancedColorInfo();
            if (info.CurrentAdvancedColorKind != Windows.Graphics.Display.AdvancedColorKind.HighDynamicRange)
                return 80f;
            var options = CaptureOptions.Current;
            return options.MatchWindowsSdrWhite ? (float)info.SdrWhiteLevelInNits : options.SdrWhiteNits;
        }
        catch (Exception ex)
        {
            Capta.Services.Log.Info($"AdvancedColorInfo unavailable: {ex.Message}");
            return 80f;
        }
    }

    private async Task<(ushort[] Pixels, int Width, int Height)> GrabAsync(GraphicsCaptureItem item)
    {
        await EnsureBorderlessAccessAsync();
        var d3d = _d3d ??= Direct3D.Create();
        try
        {
            using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                d3d.WinRTDevice, DirectXPixelFormat.R16G16B16A16Float, 1, item.Size);
            using var session = pool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;
            try { session.IsBorderRequired = false; }
            catch (Exception ex) { Capta.Services.Log.Info($"Borderless capture unavailable: {ex.Message}"); }

            var arrived = new TaskCompletionSource<(ushort[], int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
            pool.FrameArrived += (p, _) =>
            {
                using var frame = p.TryGetNextFrame();
                if (frame is null || arrived.Task.IsCompleted) return;
                try
                {
                    var pixels = d3d.ReadFp16(frame.Surface, out var w, out var h);
                    arrived.TrySetResult((pixels, w, h));
                }
                catch (Exception ex)
                {
                    arrived.TrySetException(ex);
                }
            };

            session.StartCapture();
            return await arrived.Task.WaitAsync(FrameTimeout);
        }
        catch
        {
            // Device may have been removed (driver update, GPU switch); rebuild next time.
            _d3d?.Dispose();
            _d3d = null;
            throw;
        }
    }

    private async Task EnsureBorderlessAccessAsync()
    {
        if (_borderlessRequested) return;
        _borderlessRequested = true;
        try
        {
            await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
        }
        catch (Exception ex)
        {
            Capta.Services.Log.Info($"Borderless access request failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _d3d?.Dispose();
        _d3d = null;
    }
}
