using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml;
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
    private MenuFlyoutItem? _printScreenWarning;
    private MenuFlyoutSeparator? _printScreenWarningSeparator;

    public TrayIconService(App app) => _app = app;

    public void Create()
    {
        _startupItem = new ToggleMenuFlyoutItem { Text = "Start with Windows" };
        _startupItem.Click += async (_, _) => await ToggleStartupAsync();

        var exit = new MenuFlyoutItem { Text = "Exit", Icon = new SymbolIcon(Symbol.Cancel) };
        exit.Click += (_, _) => _app.Quit();

        _printScreenWarning = new MenuFlyoutItem
        {
            Text = "Print Screen opens Snipping Tool: fix…",
            Icon = new FontIcon { Glyph = "" },
            Visibility = Visibility.Collapsed,
        };
        _printScreenWarning.Click += (_, _) => _app.ShowPrintScreenNotice();
        _printScreenWarningSeparator = new MenuFlyoutSeparator { Visibility = Visibility.Collapsed };

        var menu = new MenuFlyout
        {
            Items = { _printScreenWarning, _printScreenWarningSeparator, _startupItem, new MenuFlyoutSeparator(), exit },
        };
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

    public void SetPrintScreenWarning(bool windowsOwnsKey)
    {
        var v = windowsOwnsKey ? Visibility.Visible : Visibility.Collapsed;
        if (_printScreenWarning is not null) _printScreenWarning.Visibility = v;
        if (_printScreenWarningSeparator is not null) _printScreenWarningSeparator.Visibility = v;
        if (_icon is not null)
            _icon.ToolTipText = windowsOwnsKey ? "Capta: Print Screen is still used by Windows" : "Capta";
    }

    public void ShowWelcomeNotification() =>
        _icon?.ShowNotification("Capta is running", "Find Capta in the notification area.", NotificationIcon.None);

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
