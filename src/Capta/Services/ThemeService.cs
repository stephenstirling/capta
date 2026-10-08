using Microsoft.UI.Xaml;

namespace Capta.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Applies the chosen theme (Settings.Theme) to Capta's windows. The capture overlay always
/// stays dark and doesn't register.
/// </summary>
public static class ThemeService
{
    private static readonly List<WeakReference<Window>> s_windows = [];

    /// <summary>Call once per window, after InitializeComponent.</summary>
    public static void Register(Window window)
    {
        s_windows.Add(new WeakReference<Window>(window));
        Apply(window);
    }

    public static void SetTheme(AppTheme theme)
    {
        Settings.Theme = theme;
        s_windows.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in s_windows)
            if (weak.TryGetTarget(out var window))
                Apply(window);
    }

    private static void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = Settings.Theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }
}
