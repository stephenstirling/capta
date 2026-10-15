using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// The tray icon's right-click menu. It's Capta's own window rather than a MenuFlyout so that its rows
/// can be compact (32 px), share one icon column, keep shortcut hints beside their labels and use the
/// same minimal shadow as the other floating windows. It closes when it loses focus, like a system menu.
/// </summary>
public sealed partial class TrayMenuWindow : Window
{
    private readonly App _app;

    public TrayMenuWindow(App app)
    {
        _app = app;
        InitializeComponent();
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
        AppWindow.SetIcon("Assets/Capta.ico");
        TransparentBackdrop.PrepareWindow(this);
        CaptureExclusion.Apply(this);

        // The shadow host tracks the panel exactly; the window padding leaves room for the blur.
        Panel.SizeChanged += (_, _) =>
        {
            ShadowHost.Width = Panel.ActualWidth;
            ShadowHost.Height = Panel.ActualHeight;
        };
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 10, offsetY: 2, blur: 8, opacity: 0.22);
        Interop.ClickThroughMargins.Attach(this, Panel);

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                Hide();
        };
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void Hide() => AppWindow.Hide();

    // ---- State the tray icon keeps in step ----

    /// <summary>Shows the "Print Screen still opens Snipping Tool" row while Windows owns the key.</summary>
    public void SetPrintScreenWarning(bool windowsOwnsKey)
    {
        var visibility = windowsOwnsKey ? Visibility.Visible : Visibility.Collapsed;
        WarningRow.Visibility = WarningDivider.Visibility = visibility;
    }

    public void SetClickThroughPins(bool any) =>
        ClickThroughRow.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The tick in the icon column, and a hint when Windows Settings holds the choice.</summary>
    public async Task RefreshStartupStateAsync()
    {
        var state = await StartupTaskService.GetStateAsync();
        StartupTick.Visibility = StartupTaskService.IsEnabled(state) ? Visibility.Visible : Visibility.Collapsed;
        StartupHint.Text = StartupTaskService.IsLocked(state) ? "managed in Settings" : "";
    }

    /// <summary>Shortcut text beside the labels (display only; the hook owns the keys).</summary>
    public void UpdateShortcutHints()
    {
        RegionHint.Text = Shortcuts.For(HotkeyAction.Region).Compact;
        WindowHint.Text = Shortcuts.For(HotkeyAction.ActiveWindow).Compact;
        FullScreenHint.Text = Shortcuts.For(HotkeyAction.FullScreen).Compact;
        RecordHint.Text = Shortcuts.For(HotkeyAction.Record).Compact;
        RepeatHint.Text = Shortcuts.For(HotkeyAction.RepeatRegion).Compact;
    }

    // ---- Showing ----

    public async void ShowAtCursor()
    {
        UpdateShortcutHints();
        RepeatRow.IsEnabled = Settings.LastRegion is not null;
        SetupRow.Visibility = Settings.FirstRunDone ? Visibility.Collapsed : Visibility.Visible;
        // The Store updates Store builds, so the row (and its divider) only exist elsewhere.
        UpdatesRow.Visibility = UpdatesDivider.Visibility = AboutInfo.IsStoreBuild ? Visibility.Collapsed : Visibility.Visible;
        await RefreshStartupStateAsync(); // before sizing: its hint changes the width

        var cursor = WindowHelper.CursorPosition();
        var area = WindowHelper.CursorWorkArea();
        // Move onto the cursor's monitor first so the window adopts its DPI before sizing.
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();

        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        this.ResizeDip(Root.DesiredSize.Width, Root.DesiredSize.Height);
        var size = AppWindow.Size;
        var pad = Root.Padding;

        // The menu opens up and to the right of the cursor (the tray is usually at the bottom), or to
        // its left / below it when there's no room, and always stays inside the work area.
        var x = cursor.X - (int)(pad.Left * scale);
        if (x + size.Width > area.X + area.Width)
            x = cursor.X - size.Width + (int)(pad.Right * scale);
        var y = cursor.Y - size.Height + (int)(pad.Bottom * scale);
        if (y < area.Y)
            y = cursor.Y - (int)(pad.Top * scale);
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - size.Width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height));
        AppWindow.Move(new PointInt32(x, y));
        this.BringToFront();
    }

    // ---- Rows ----

    /// <summary>Closes the menu, then lets it finish closing before the action (a capture freezes the screen).</summary>
    private void Do(Action action)
    {
        Hide();
        _app.Dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => action());
    }

    private void OnRegion(object sender, RoutedEventArgs e) => Do(() => _app.StartCapture(CaptureMode.Region));

    private void OnWindow(object sender, RoutedEventArgs e) => Do(() => _app.StartCapture(CaptureMode.Window));

    private void OnFullScreen(object sender, RoutedEventArgs e) => Do(() => _app.StartCapture(CaptureMode.FullScreen));

    private void OnRecord(object sender, RoutedEventArgs e) => Do(() => _app.StartCapture(CaptureMode.Recording));

    private void OnRepeatRegion(object sender, RoutedEventArgs e) => Do(_app.RepeatLastRegion);

    private void OnShowToolbar(object sender, RoutedEventArgs e) => Do(_app.ShowToolbar);

    private void OnReleasePins(object sender, RoutedEventArgs e) => Do(_app.ReleaseClickThroughPins);

    private async void OnStartWithWindows(object sender, RoutedEventArgs e)
    {
        Hide();
        var current = await StartupTaskService.GetStateAsync();
        if (StartupTaskService.IsLocked(current))
        {
            // Only the user can undo DisabledByUser; send them to the right page.
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
            return;
        }
        await StartupTaskService.SetEnabledAsync(!StartupTaskService.IsEnabled(current));
    }

    private void OnSettings(object sender, RoutedEventArgs e) => Do(_app.ShowSettings);

    private void OnFixPrintScreen(object sender, RoutedEventArgs e) => Do(_app.ShowSettings);

    private void OnSetup(object sender, RoutedEventArgs e) => Do(_app.ShowFirstRun);

    private void OnCheckForUpdates(object sender, RoutedEventArgs e) => Do(_app.CheckForUpdates);

    private void OnAbout(object sender, RoutedEventArgs e) => Do(_app.ShowAbout);

    private void OnExit(object sender, RoutedEventArgs e) => Do(_app.Quit);

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }
}
