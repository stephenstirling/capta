using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// Shows the tray icon's right-click menu. H.NotifyIcon's SecondWindow mode sizes its window
/// from the menu items before they are laid out, which squeezed out the labels and added a
/// scroll bar. Here the menu is a windowed popup (not limited to its window's bounds), so WinUI
/// sizes it and keeps it on screen. The window itself is an invisible 1 × 1 anchor at the
/// cursor that holds focus while the menu is open.
/// </summary>
public sealed class TrayMenuWindow : Window
{
    private readonly MenuFlyout _menu;
    private readonly Grid _anchor = new();

    public TrayMenuWindow(MenuFlyout menu)
    {
        _menu = menu;
        _menu.ShouldConstrainToRootBounds = false;
        _menu.Closed += (_, _) => AppWindow.Hide();

        Content = _anchor;
        ThemeService.Register(this);
        ExtendsContentIntoTitleBar = true;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        TransparentBackdrop.PrepareWindow(this);

        // Close the menu when another app takes the focus, as a system menu does. The menu's
        // own popup belongs to this process, so moving into it doesn't count.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!WindowHelper.IsForegroundOurs()) _menu.Hide();
            });
        };
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            _menu.Hide();
        };
    }

    public void ShowAtCursor()
    {
        var cursor = WindowHelper.CursorPosition();
        AppWindow.MoveAndResize(new RectInt32(cursor.X, cursor.Y, 1, 1));
        this.BringToFront();

        // The first time, the anchor has no XamlRoot until the window has loaded.
        if (_anchor.IsLoaded)
        {
            Open();
        }
        else
        {
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                _anchor.Loaded -= OnLoaded;
                Open();
            }
            _anchor.Loaded += OnLoaded;
        }
    }

    private void Open() => _menu.ShowAt(_anchor, new FlyoutShowOptions
    {
        Position = new Point(0, 0),
        ShowMode = FlyoutShowMode.Standard,
    });
}
