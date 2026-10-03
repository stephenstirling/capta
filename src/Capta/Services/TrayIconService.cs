using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;
using Windows.System;

namespace Capta.Services;

/// <summary>Owns the notification-area icon and its Fluent context menu.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly App _app;
    private TaskbarIcon? _icon;
    private ToggleMenuFlyoutItem? _startupItem;

    public TrayIconService(App app) => _app = app;

    public void Create()
    {
        _startupItem = new ToggleMenuFlyoutItem { Text = "Start with Windows" };
        _startupItem.Click += async (_, _) => await ToggleStartupAsync();

        var exit = new MenuFlyoutItem { Text = "Exit", Icon = new SymbolIcon(Symbol.Cancel) };
        exit.Click += (_, _) => _app.Quit();

        var menu = new MenuFlyout { Items = { _startupItem, new MenuFlyoutSeparator(), exit } };
        menu.Opening += async (_, _) => await RefreshStartupStateAsync();

        _icon = new TaskbarIcon
        {
            ToolTipText = "Capta",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Tray.ico")),
            ContextMenuMode = ContextMenuMode.SecondWindow,
            NoLeftClickDelay = true,
            ContextFlyout = menu,
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public async Task RefreshStartupStateAsync()
    {
        if (_startupItem is null) return;
        var state = await StartupTaskService.GetStateAsync();
        _startupItem.IsChecked = StartupTaskService.IsEnabled(state);
        _startupItem.Text = StartupTaskService.IsLocked(state)
            ? "Start with Windows (managed in Settings)"
            : "Start with Windows";
    }

    private async Task ToggleStartupAsync()
    {
        var current = await StartupTaskService.GetStateAsync();
        if (StartupTaskService.IsLocked(current))
        {
            // Only the user can undo DisabledByUser; send them to the right page.
            await Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
        }
        else
        {
            await StartupTaskService.SetEnabledAsync(!StartupTaskService.IsEnabled(current));
        }
        await RefreshStartupStateAsync();
    }

    public void ShowWelcomeNotification() =>
        _icon?.ShowNotification("Capta is running", "Find Capta in the notification area.", NotificationIcon.None);

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
