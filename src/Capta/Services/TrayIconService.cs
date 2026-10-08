using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;

namespace Capta.Services;

/// <summary>Owns the notification-area icon and its Fluent context menu.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly App _app;
    private TaskbarIcon? _icon;
    private ToggleMenuFlyoutItem? _startupItem;
    private MenuFlyoutItem? _printScreenWarning;
    private MenuFlyoutSeparator? _printScreenWarningSeparator;
    private MenuFlyoutItem? _clickThroughItem;

    public TrayIconService(App app) => _app = app;

    public void Create()
    {
        _startupItem = new ToggleMenuFlyoutItem { Text = "Start with Windows" };
        _startupItem.Click += async (_, _) => await ToggleStartupAsync();

        var exit = new MenuFlyoutItem { Text = "Exit", Icon = new SymbolIcon(Symbol.Cancel) };
        exit.Click += (_, _) => _app.Quit();

        var toolbar = new MenuFlyoutItem
        {
            Text = "Show toolbar",
            Icon = new FontIcon { Glyph = "" },
            KeyboardAcceleratorTextOverride = "Ctrl+PrtSc",
        };
        toolbar.Click += (_, _) => _app.ShowToolbar();

        _printScreenWarning = new MenuFlyoutItem
        {
            Text = "Print Screen opens Snipping Tool: fix…",
            Icon = new FontIcon { Glyph = "" },
            Visibility = Visibility.Collapsed,
        };
        _printScreenWarning.Click += (_, _) => _app.ShowSettings();

        var settings = new MenuFlyoutItem { Text = "Startup & shortcuts…", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => _app.ShowSettings();
        _printScreenWarningSeparator = new MenuFlyoutSeparator { Visibility = Visibility.Collapsed };

        _clickThroughItem = new MenuFlyoutItem
        {
            Text = "Make pinned captures clickable",
            Icon = new FontIcon { Glyph = "" },
            Visibility = Visibility.Collapsed,
        };
        _clickThroughItem.Click += (_, _) => _app.ReleaseClickThroughPins();

        var menu = new MenuFlyout
        {
            Items =
            {
                _printScreenWarning, _printScreenWarningSeparator,
                CaptureItem("Region", "", "PrtSc", CaptureMode.Region),
                CaptureItem("Window", "", "", CaptureMode.Window),
                CaptureItem("Full screen", "", "Shift+PrtSc", CaptureMode.FullScreen),
                toolbar,
                _clickThroughItem,
                new MenuFlyoutSeparator(),
                _startupItem, settings, new MenuFlyoutSeparator(), exit,
            },
        };
        menu.Opening += async (_, _) => await RefreshStartupStateAsync();

        _icon = new TaskbarIcon
        {
            ToolTipText = "Capta",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Tray.ico")),
            ContextMenuMode = ContextMenuMode.SecondWindow,
            NoLeftClickDelay = true,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(_app.ShowTrayFlyout),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    private MenuFlyoutItem CaptureItem(string text, string glyph, string shortcut, CaptureMode mode)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
            KeyboardAcceleratorTextOverride = shortcut, // display only; the hook owns the keys
        };
        // Let the menu window close before the overlay freezes the screen.
        item.Click += (_, _) => _app.Dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => _app.StartCapture(mode));
        return item;
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

    public void SetClickThroughPins(bool any)
    {
        if (_clickThroughItem is not null)
            _clickThroughItem.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowError(string title, string message) =>
        _icon?.ShowNotification(title, message, NotificationIcon.Error);

    private sealed class RelayCommand(Action action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
