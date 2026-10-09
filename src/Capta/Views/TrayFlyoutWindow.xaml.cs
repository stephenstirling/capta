using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// Flyout from the tray icon (design/mockups/Tray.html): capture tiles, delay, recent
/// captures and Ocula. Opens bottom-right above the taskbar and hides when it loses focus.
/// </summary>
public sealed partial class TrayFlyoutWindow : Window
{
    /// <summary>Gap between the panel and the work-area corner.</summary>
    private const double MarginDip = 12;

    private readonly App _app;

    public TrayFlyoutWindow(App app)
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

        ShadowHost.Width = Panel.Width;
        ShadowHost.Height = Panel.Height;
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 14, offsetY: 16, blur: 40, opacity: 0.45);
        Interop.ClickThroughMargins.Attach(this, Panel);

        // Behave like a system flyout: clicking elsewhere dismisses it.
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

    public async void ShowNearTray(IReadOnlyList<CaptureResult> recent)
    {
        ApplyDelay(Settings.CaptureDelaySeconds);
        RegionKeys.Text = Shortcuts.For(HotkeyAction.Region).Compact;
        WindowKeys.Text = Shortcuts.For(HotkeyAction.ActiveWindow).Compact;
        OculaButton.IsEnabled = await CaptureActions.IsOculaInstalledAsync();
        ToolTipService.SetToolTip(OculaButton, OculaButton.IsEnabled ? null : "Ocula isn't installed yet");
        await FillRecentAsync(recent);

        // Bottom-right of the work area on the cursor's monitor (the tray is there by default).
        var area = WindowHelper.CursorWorkArea();
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();
        var pad = Root.Padding;
        this.ResizeDip(Panel.Width + pad.Left + pad.Right, Panel.Height + pad.Top + pad.Bottom);
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            area.X + area.Width - size.Width - (int)((MarginDip - pad.Right) * scale),
            area.Y + area.Height - size.Height - (int)((MarginDip - pad.Bottom) * scale)));
        this.BringToFront();
    }

    public void Hide() => AppWindow.Hide();

    // ---- Recent ----

    private async Task FillRecentAsync(IReadOnlyList<CaptureResult> recent)
    {
        RecentGrid.Children.Clear();
        for (var i = 0; i < CaptureHistory.Capacity; i++)
        {
            var cell = new Border { Style = (Style)Root.Resources["RecentCell"] };
            Grid.SetRow(cell, i / 3);
            Grid.SetColumn(cell, i % 3);

            if (i < recent.Count)
            {
                var result = recent[i];
                var source = new SoftwareBitmapSource();
                await source.SetBitmapAsync(result.Image.ToSoftwareBitmap());
                cell.Background = new ImageBrush { ImageSource = source, Stretch = Stretch.UniformToFill };
                // Transparent until hovered (Styles/Floating.xaml), so the thumbnail shows through.
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                };
                ToolTipService.SetToolTip(button, result.IsVideo
                    ? $"Recording · {result.VideoLength:m\\:ss}"
                    : $"{result.ModeName} · {result.Image.Width} × {result.Image.Height}");
                if (result.IsVideo)
                {
                    button.Content = new Border
                    {
                        Margin = new Thickness(0, 0, 0, 0),
                        Padding = new Thickness(6, 2, 6, 2),
                        CornerRadius = new CornerRadius(5),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CaptaVideoBadgeBrush"],
                        Child = new TextBlock
                        {
                            Text = result.VideoLength.TotalHours >= 1 ? result.VideoLength.ToString(@"h\:mm\:ss") : result.VideoLength.ToString(@"m\:ss"),
                            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["CaptaMonoFont"],
                            FontSize = 11,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CaptaVideoBadgeTextBrush"],
                        },
                    };
                    button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    button.VerticalContentAlignment = VerticalAlignment.Stretch;
                    button.Padding = new Thickness(8);
                }
                button.Click += (_, _) =>
                {
                    Hide();
                    _app.ShowCard(result);
                };
                cell.Child = button;
            }
            RecentGrid.Children.Add(cell);
        }
    }

    // ---- Tiles, delay, actions ----

    private void Capture(CaptureMode mode)
    {
        Hide();
        // Let the flyout disappear before the screen is frozen.
        _app.Dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => _app.StartCapture(mode, Settings.CaptureDelaySeconds));
    }

    private void OnRegion(object sender, RoutedEventArgs e) => Capture(CaptureMode.Region);
    private void OnWindow(object sender, RoutedEventArgs e) => Capture(CaptureMode.Window);
    private void OnFullScreen(object sender, RoutedEventArgs e) => Capture(CaptureMode.FullScreen);
    private void OnRecord(object sender, RoutedEventArgs e) => Capture(CaptureMode.Recording);

    private void ApplyDelay(int seconds)
    {
        foreach (var segment in new[] { DelayOff, Delay3, Delay5, Delay10 })
        {
            var selected = int.Parse((string)segment.Tag) == seconds;
            segment.IsChecked = selected;
            segment.FontWeight = selected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void OnDelay(object sender, RoutedEventArgs e)
    {
        var seconds = int.Parse((string)((ToggleButton)sender).Tag);
        Settings.CaptureDelaySeconds = seconds;
        ApplyDelay(seconds); // also re-checks the clicked segment so one is always selected
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        Hide();
        _app.ShowSettings();
    }

    private async void OnOpenInOcula(object sender, RoutedEventArgs e)
    {
        Hide();
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ocula:captures"));
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }
}
