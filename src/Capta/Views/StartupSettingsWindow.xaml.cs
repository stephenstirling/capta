using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// Startup &amp; shortcuts (design/mockups/Startup.html). Also where Capta explains the
/// Print Screen conflict with Snipping Tool. Reused: hidden rather than closed.
/// </summary>
public sealed partial class StartupSettingsWindow : Window
{
    private const double PanelHeightDip = 900;

    private readonly App _app;
    private bool _loading;

    public StartupSettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleRow);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.SetIcon("Assets/Capta.ico");
        TransparentBackdrop.PrepareWindow(this);

        Panel.SizeChanged += (_, _) =>
        {
            ShadowHost.Width = Panel.ActualWidth;
            ShadowHost.Height = Panel.ActualHeight;
        };
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 14, offsetY: 20, blur: 50, opacity: 0.5);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            AppWindow.Hide();
        };
    }

    public async void ShowCentered(bool windowsOwnsPrintScreen)
    {
        await LoadAsync(windowsOwnsPrintScreen);

        var area = WindowHelper.CursorWorkArea();
        // Move onto the target monitor first so the window adopts its DPI before sizing.
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();
        var pad = Root.Padding;

        // 900px tall per the mockup, shorter on small screens (the content scrolls).
        var maxPanel = area.Height / scale - pad.Top - pad.Bottom - 16;
        Panel.Height = Math.Min(PanelHeightDip, maxPanel);
        this.ResizeDip(Panel.Width + pad.Left + pad.Right, Panel.Height + pad.Top + pad.Bottom);
        this.CenterOnCursorMonitor();
        this.BringToFront();
    }

    private async Task LoadAsync(bool windowsOwnsPrintScreen)
    {
        _loading = true;
        try
        {
            StartWithWindows.IsOn = StartupTaskService.IsEnabled(await StartupTaskService.GetStateAsync());
            StartQuietly.IsOn = Settings.StartQuietly;
            ShowToolbarAtStartup.IsOn = Settings.ShowToolbarAtStartup;
            KeepRunning.IsOn = Settings.KeepRunningWhenToolbarClosed;
            SetPrintScreenOwnership(windowsOwnsPrintScreen);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Shows the warning card while Windows owns Print Screen, otherwise the success card.</summary>
    public void SetPrintScreenOwnership(bool windowsOwnsKey)
    {
        ConflictCard.Visibility = windowsOwnsKey ? Visibility.Visible : Visibility.Collapsed;
        OwnedCard.Visibility = windowsOwnsKey ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Startup ----

    private async void OnStartWithWindows(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var current = await StartupTaskService.GetStateAsync();
        if (StartupTaskService.IsLocked(current))
        {
            // Only the user can undo DisabledByUser (or policy); send them to the right page.
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
        }
        else
        {
            await StartupTaskService.SetEnabledAsync(StartWithWindows.IsOn);
        }

        _loading = true;
        StartWithWindows.IsOn = StartupTaskService.IsEnabled(await StartupTaskService.GetStateAsync());
        _loading = false;
        await _app.RefreshTrayAsync();
    }

    private void OnStartQuietly(object sender, RoutedEventArgs e)
    {
        if (!_loading) Settings.StartQuietly = StartQuietly.IsOn;
    }

    private void OnShowToolbarAtStartup(object sender, RoutedEventArgs e)
    {
        if (!_loading) Settings.ShowToolbarAtStartup = ShowToolbarAtStartup.IsOn;
    }

    private void OnKeepRunning(object sender, RoutedEventArgs e)
    {
        if (!_loading) Settings.KeepRunningWhenToolbarClosed = KeepRunning.IsOn;
    }

    // ---- Print Screen, links, close ----

    private async void OnOpenKeyboardSettings(object sender, RoutedEventArgs e) =>
        await PrintScreenOwnership.OpenSettingsAsync();

    private async void OnGitHub(object sender, RoutedEventArgs e) => await Links.OpenAsync(Links.GitHub);

    private async void OnBuyMeACoffee(object sender, RoutedEventArgs e) => await Links.OpenAsync(Links.BuyMeACoffee);

    private void OnDone(object sender, RoutedEventArgs e) => AppWindow.Hide();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AppWindow.Hide();
    }
}
