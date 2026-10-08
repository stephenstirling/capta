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

    /// <summary>Launch without opening a window (Print Screen still works). On by default.</summary>
    public static bool StartQuietly
    {
        get => Get(nameof(StartQuietly), true);
        set => Values[nameof(StartQuietly)] = value;
    }

    public static bool ShowToolbarAtStartup
    {
        get => Get(nameof(ShowToolbarAtStartup), false);
        set => Values[nameof(ShowToolbarAtStartup)] = value;
    }

    /// <summary>When off, closing the toolbar (×) exits Capta. Hide to tray always keeps it running.</summary>
    public static bool KeepRunningWhenToolbarClosed
    {
        get => Get(nameof(KeepRunningWhenToolbarClosed), true);
        set => Values[nameof(KeepRunningWhenToolbarClosed)] = value;
    }

    /// <summary>Set once first-run setup has been completed or closed.</summary>
    public static bool FirstRunDone
    {
        get => Get(nameof(FirstRunDone), false);
        set => Values[nameof(FirstRunDone)] = value;
    }

    public static AppTheme Theme
    {
        get => (AppTheme)Get(nameof(Theme), (int)AppTheme.System);
        set => Values[nameof(Theme)] = (int)value;
    }

    /// <summary>Where captures are saved (Save As starts here). Default: Pictures\Captures.</summary>
    public static string CaptureFolder
    {
        get => Get(nameof(CaptureFolder), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Captures"));
        set => Values[nameof(CaptureFolder)] = value;
    }

    /// <summary>Send every capture to Ocula when it's installed.</summary>
    public static bool SendToOculaAutomatically
    {
        get => Get(nameof(SendToOculaAutomatically), true);
        set => Values[nameof(SendToOculaAutomatically)] = value;
    }

    private static T Get<T>(string key, T fallback) => Values.TryGetValue(key, out var v) && v is T t ? t : fallback;
}
