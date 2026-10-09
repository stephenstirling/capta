using System.Runtime.InteropServices;
using Capta.Services;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Capture;

/// <summary>
/// Scrolling capture: scrolls the content under a region with the mouse wheel, captures it
/// after each step and stitches the frames (<see cref="ScrollStitcher"/>). Stops when the
/// content stops moving, when Esc is pressed, or at <see cref="MaxHeight"/>.
/// </summary>
internal static partial class ScrollingCapture
{
    private const int MaxHeight = 30000;
    private const int MaxSteps = 200;
    private const int WheelStep = -240;        // two notches down per step
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(220);
    private const int VK_ESCAPE = 0x1B;

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public int mouseData;
        public uint dwFlags, time;
        public nint dwExtraInfo;
    }

    /// <summary>INPUT with its union as MOUSEINPUT, the largest member (40 bytes on x64, as SendInput expects).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, ref INPUT inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    /// <param name="screen">The region on the virtual desktop, inside <paramref name="monitor"/>.</param>
    public static async Task<CapturedImage> RunAsync(IScreenSource source, MonitorInfo monitor, RectInt32 screen)
    {
        var local = new RectInt32(screen.X - monitor.Bounds.X, screen.Y - monitor.Bounds.Y, screen.Width, screen.Height);
        var restore = Interop.WindowHelper.CursorPosition();
        var centre = new PointInt32(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);

        // Let the overlay disappear before the first frame.
        await Task.Delay(Settle);
        var stitcher = new ScrollStitcher(await GrabAsync(source, monitor, local));
        var still = 0;
        var steps = 0;
        try
        {
            while (steps++ < MaxSteps && stitcher.Height < MaxHeight)
            {
                if (IsKeyDown(VK_ESCAPE))
                {
                    Log.Info("Scrolling capture stopped with Esc");
                    break;
                }
                SetCursorPos(centre.X, centre.Y);
                Wheel(WheelStep);
                await Task.Delay(Settle);

                var added = stitcher.Add(await GrabAsync(source, monitor, local));
                if (added > 0)
                {
                    still = 0;
                    continue;
                }
                // Smooth scrolling can lag a step; give it one more chance before stopping.
                if (++still >= 2)
                {
                    if (added < 0) Log.Info("Scrolling capture: frames stopped matching");
                    break;
                }
            }
        }
        finally
        {
            SetCursorPos(restore.X, restore.Y);
        }
        var image = stitcher.Build();
        Log.Info($"Scrolling capture: {steps} steps, {image.Width}x{image.Height}");
        return image;
    }

    private static async Task<CapturedImage> GrabAsync(IScreenSource source, MonitorInfo monitor, RectInt32 local) =>
        (await source.CaptureMonitorAsync(monitor)).Crop(local);

    private static void Wheel(int delta)
    {
        var input = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { mouseData = delta, dwFlags = MOUSEEVENTF_WHEEL } };
        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }
}
