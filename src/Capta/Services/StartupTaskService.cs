using Windows.ApplicationModel;
using Windows.Storage;

namespace Capta.Services;

/// <summary>Wraps the windows.startupTask declared in Package.appxmanifest.</summary>
public static class StartupTaskService
{
    private const string TaskId = "CaptaStartup";
    private const string FirstRunKey = "StartupTask.FirstRunHandled";

    public static async Task<StartupTaskState> GetStateAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return task.State;
    }

    /// <summary>
    /// The manifest declares Enabled="true", but Windows only honours that once the
    /// app has run. On first run, request enablement explicitly; never override a
    /// user's later choice in Settings > Apps > Startup.
    /// </summary>
    public static async Task EnsureEnabledOnFirstRunAsync()
    {
        var settings = ApplicationData.Current.LocalSettings.Values;
        if (settings.ContainsKey(FirstRunKey))
            return;

        var task = await StartupTask.GetAsync(TaskId);
        if (task.State == StartupTaskState.Disabled)
            await task.RequestEnableAsync();
        settings[FirstRunKey] = true;
    }

    /// <returns>The resulting state; may be DisabledByUser/DisabledByPolicy, which only Settings can change.</returns>
    public static async Task<StartupTaskState> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled)
            return await task.RequestEnableAsync();
        task.Disable();
        return task.State;
    }

    public static bool IsEnabled(StartupTaskState s) =>
        s is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    public static bool IsLocked(StartupTaskState s) =>
        s is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy;
}
