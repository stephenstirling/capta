using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Views;

/// <summary>
/// After-capture card (design/mockups/Toolbar.html): bottom-right of the capture's monitor,
/// auto-hides after 6 s unless hovered or busy. Reused across captures (hidden, never closed).
/// </summary>
public sealed partial class CaptureCardWindow : Window
{
    /// <summary>Gap between the panel and the work-area edges.</summary>
    private const double MarginDip = 24;
    private static readonly TimeSpan AutoHide = TimeSpan.FromSeconds(6);

    private readonly DispatcherQueueTimer _timer;
    private CaptureResult? _current;
    private bool _busy;
    private bool _hovered;

    public event Action<CaptureResult>? PinRequested;

    private Flyout CopyMenu => (Flyout)CopySplit.Resources["CopyMenuFlyout"];

    public CaptureCardWindow()
    {
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

        Panel.SizeChanged += (_, _) =>
        {
            ShadowHost.Width = Panel.ActualWidth;
            ShadowHost.Height = Panel.ActualHeight;
            // The first measure can run before the content has laid out; re-anchor to the real size.
            if (_current is not null && AppWindow.IsVisible)
                PlaceOn(_current.Monitor);
        };
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 16, offsetY: 20, blur: 50, opacity: 0.55);
        Interop.ClickThroughMargins.Attach(this, Panel);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        CopyMenu.Opening += (_, _) =>
        {
            AutoCopySwitch.IsOn = Settings.AutoCopy;
            CopyPathRow.IsEnabled = _current?.SavedPath is not null;
            _busy = true;
            _timer!.Stop();
        };
        CopyMenu.Closed += (_, _) =>
        {
            _busy = false;
            if (!_hovered) RestartTimer();
        };

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = AutoHide;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Hide();
    }

    /// <param name="status">Overrides the status line (e.g. Grab text's result).</param>
    public async void Show(CaptureResult result, bool copied, string? status = null, bool success = true)
    {
        _current = result;
        var image = result.Image;
        DetailText.Text = $"{result.ModeName} · {image.Width} × {image.Height}" + (result.WasHdr ? " · tone-mapped from HDR" : "");
        if (status is not null)
            SetStatus(status, success);
        else
            SetStatus(copied ? "Copied to clipboard" : "Capture ready", success: copied);

        var oculaInstalled = await CaptureActions.IsOculaInstalledAsync();
        OculaButton.IsEnabled = oculaInstalled;
        ToolTipService.SetToolTip(OculaButton, oculaInstalled ? "Send to Ocula" : "Send to Ocula (Ocula isn't installed)");

        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(image.ToSoftwareBitmap());
        if (_current != result) return; // superseded by a newer capture
        Thumbnail.Source = source;

        PlaceOn(result.Monitor);
        this.BringToFront();
        RestartTimer();
    }

    /// <summary>Bottom-right of the monitor's work area; the window's padding holds the shadow.</summary>
    private void PlaceOn(MonitorInfo monitor)
    {
        var area = monitor.WorkArea;
        // Move first so the window adopts the target monitor's DPI before sizing.
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();

        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var pad = Root.Padding;
        var width = Math.Max(Root.DesiredSize.Width, Panel.ActualWidth + pad.Left + pad.Right);
        var height = Math.Max(Root.DesiredSize.Height, Panel.ActualHeight + pad.Top + pad.Bottom);
        // Size the client area: even a borderless window keeps a thin frame in its outer size.
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)));
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            area.X + area.Width - size.Width - (int)((MarginDip - pad.Right) * scale),
            area.Y + area.Height - size.Height - (int)((MarginDip - pad.Bottom) * scale)));
    }

    public void Hide()
    {
        _timer.Stop();
        CopyMenu.Hide();
        AppWindow.Hide();
    }

    private void RestartTimer()
    {
        _timer.Stop();
        _timer.Start();
    }

    private void SetStatus(string text, bool success = true)
    {
        StatusText.Text = text;
        SuccessDot.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        NeutralDot.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Hover keeps the card open ----

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        _timer.Stop();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        if (!_busy) RestartTimer();
    }

    // ---- Copy ----

    private async void OnCopy(object sender, RoutedEventArgs e) => await CopyImageAsync();

    private void OnCopyMenuClick(object sender, RoutedEventArgs e) => CopyMenu.ShowAt(CopySplit);

    private async void OnCopyImage(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        await CopyImageAsync();
    }

    private async Task CopyImageAsync()
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            await ImageExport.CopyToClipboardAsync(_current.Image);
            SetStatus("Copied to clipboard");
        });
    }

    private async void OnCopyText(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        await CopyTextAsync();
    }

    private async Task CopyTextAsync()
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            var text = await CaptureActions.CopyTextInImageAsync(_current.Image);
            if (text is null)
            {
                SetStatus("No text found", success: false);
                return;
            }
            var lines = text.Split(Environment.NewLine).Length;
            SetStatus(lines == 1 ? "Copied 1 line of text" : $"Copied {lines} lines of text");
        });
    }

    private async void OnCopyAsFile(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        if (_current is null) return;
        await RunAsync(async () =>
        {
            await CaptureActions.CopyAsFileAsync(_current);
            SetStatus("Copied as file");
        });
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        if (_current?.SavedPath is not { } path) return;
        CaptureActions.CopyText(path);
        SetStatus("Copied file path");
        RestartTimer();
    }

    private void OnAutoCopyToggled(object sender, RoutedEventArgs e) => Settings.AutoCopy = AutoCopySwitch.IsOn;

    private async void OnCopyAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CopyImageAsync();
    }

    private async void OnCopyTextAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CopyTextAsync();
    }

    // ---- Edit, Save, Pin, Ocula ----

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            await CaptureActions.EditAsync(_current);
            Hide();
        });
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            var path = await CaptureActions.SaveAsAsync(_current, AppWindow.Id);
            if (path is null) return;
            _current.SavedPath = path;
            ((App)Application.Current).History.Update();
            SetStatus($"Saved {Path.GetFileName(path)}");
        });
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        PinRequested?.Invoke(_current);
        Hide();
    }

    private async void OnSendToOcula(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            await CaptureActions.SendToOculaAsync(_current);
            SetStatus("Sent to Ocula");
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }

    /// <summary>Holds the card open while an action (e.g. a dialog) is in progress.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        _timer.Stop();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error("Card action failed", ex);
            SetStatus(ex.Message, success: false);
        }
        finally
        {
            _busy = false;
            if (!_hovered) RestartTimer();
        }
    }
}
