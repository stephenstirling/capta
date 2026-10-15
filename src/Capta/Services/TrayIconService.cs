using Capta.Views;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Capta.Services;

/// <summary>Owns the notification-area icon; its right-click menu is <see cref="TrayMenuWindow"/>.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly App _app;
    private TaskbarIcon? _icon;
    private TrayMenuWindow? _menu;
    private bool _windowsOwnsKey;
    private bool _anyClickThroughPins;

    public TrayIconService(App app) => _app = app;

    public void Create()
    {
        Shortcuts.Changed += () => _menu?.UpdateShortcutHints();

        _icon = new TaskbarIcon
        {
            ToolTipText = "Capta",
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/Tray.ico")),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(_app.ShowTrayFlyout),
            RightClickCommand = new RelayCommand(ShowMenu),
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>Capta shows the menu itself, so it can size and style it (see TrayMenuWindow).</summary>
    private void ShowMenu()
    {
        if (_menu is null)
        {
            _menu = new TrayMenuWindow(_app);
            _menu.SetPrintScreenWarning(_windowsOwnsKey);
            _menu.SetClickThroughPins(_anyClickThroughPins);
        }
        _menu.ShowAtCursor();
    }

    public Task RefreshStartupStateAsync() => _menu?.RefreshStartupStateAsync() ?? Task.CompletedTask;

    public void SetPrintScreenWarning(bool windowsOwnsKey)
    {
        _windowsOwnsKey = windowsOwnsKey;
        _menu?.SetPrintScreenWarning(windowsOwnsKey);
        if (_icon is not null)
            _icon.ToolTipText = windowsOwnsKey ? "Capta: Print Screen is still used by Windows" : "Capta";
    }

    public void SetClickThroughPins(bool any)
    {
        _anyClickThroughPins = any;
        _menu?.SetClickThroughPins(any);
    }

    public void ShowInfo(string title, string message) =>
        _icon?.ShowNotification(title, message, NotificationIcon.Info);

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
        _menu?.Close();
        _menu = null;
    }
}
