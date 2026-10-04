using Windows.Foundation.Collections;
using Windows.Storage;

namespace Capta.Services;

/// <summary>User preferences, persisted in the package's LocalSettings.</summary>
public static class Settings
{
    private static IPropertySet Values => ApplicationData.Current.LocalSettings.Values;

    /// <summary>Copy each capture to the clipboard as soon as it's taken. On by default.</summary>
    public static bool AutoCopy
    {
        get => Get(nameof(AutoCopy), true);
        set => Values[nameof(AutoCopy)] = value;
    }

    /// <summary>Delay before a toolbar capture starts, in seconds (0 = off).</summary>
    public static int CaptureDelaySeconds
    {
        get => Get(nameof(CaptureDelaySeconds), 0);
        set => Values[nameof(CaptureDelaySeconds)] = value;
    }

    /// <summary>The mode selected in the toolbar's mode group; New captures in this mode.</summary>
    public static CaptureMode ToolbarMode
    {
        get => (CaptureMode)Get(nameof(ToolbarMode), (int)CaptureMode.Region);
        set => Values[nameof(ToolbarMode)] = (int)value;
    }

    public static bool ToolbarKeepOnTop
    {
        get => Get(nameof(ToolbarKeepOnTop), true);
        set => Values[nameof(ToolbarKeepOnTop)] = value;
    }

    private static T Get<T>(string key, T fallback) => Values.TryGetValue(key, out var v) && v is T t ? t : fallback;
}
