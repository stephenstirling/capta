using Microsoft.UI;
using DisplayId = Microsoft.UI.DisplayId;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Capta.Capture;

/// <summary>A connected display, in physical (virtual-desktop) pixels.</summary>
public sealed record MonitorInfo(DisplayId DisplayId, nint Handle, RectInt32 Bounds, RectInt32 WorkArea)
{
    public bool Contains(PointInt32 p) =>
        p.X >= Bounds.X && p.Y >= Bounds.Y && p.X < Bounds.X + Bounds.Width && p.Y < Bounds.Y + Bounds.Height;
}

public static class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var areas = DisplayArea.FindAll();
        var list = new List<MonitorInfo>(areas.Count);
        // Index rather than foreach: enumerating FindAll()'s projection is unreliable.
        for (var i = 0; i < areas.Count; i++)
        {
            var a = areas[i];
            list.Add(new MonitorInfo(a.DisplayId, Win32Interop.GetMonitorFromDisplayId(a.DisplayId), a.OuterBounds, a.WorkArea));
        }
        return list;
    }

    public static MonitorInfo AtPoint(PointInt32 p)
    {
        var a = DisplayArea.GetFromPoint(p, DisplayAreaFallback.Nearest);
        return new MonitorInfo(a.DisplayId, Win32Interop.GetMonitorFromDisplayId(a.DisplayId), a.OuterBounds, a.WorkArea);
    }
}
