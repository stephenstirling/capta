using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Views;

/// <summary>
/// Post-capture card: bottom-right of the capture's monitor, auto-hides after 6 s
/// unless hovered. Reused across captures (hidden, never closed).
/// </summary>
public sealed partial class CaptureCardWindow : Window
{
    private const double WidthDip = 340;
    private const double MarginDip = 12;
    private static readonly TimeSpan AutoHide = TimeSpan.FromSeconds(6);

    private readonly DispatcherQueueTimer _timer;
    private CaptureResult? _current;
    private bool _busy;

    public event Action<CaptureResult>? PinRequested;

    public CaptureCardWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets/Capta.ico");
        SetWindowDisplayAffinity(this.GetHwnd(), WDA_EXCLUDEFROMCAPTURE);
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = AutoHide;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Hide();
    }

    public async void Show(CaptureResult result, bool copied)
    {
        _current = result;
        var image = result.Image;
        SizeText.Text = $"{image.Width} × {image.Height}";
        SetStatus("", copied ? "Copied to clipboard" : "Capture ready");

        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(image.ToSoftwareBitmap());
        if (_current != result) return; // superseded by a newer capture
        Thumbnail.Source = source;

        PlaceOn(result.Monitor);
        this.BringToFront();
        RestartTimer();
    }

    private void PlaceOn(MonitorInfo monitor)
    {
        // Move first so the window adopts the target monitor's DPI before sizing.
        var area = monitor.WorkArea;
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();

        Root.Width = WidthDip;
        Root.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        var size = new SizeInt32((int)Math.Ceiling(WidthDip * scale), (int)Math.Ceiling(Root.DesiredSize.Height * scale));
        var margin = (int)(MarginDip * scale);
        AppWindow.MoveAndResize(new RectInt32(
            area.X + area.Width - size.Width - margin,
            area.Y + area.Height - size.Height - margin,
            size.Width, size.Height));
    }

    public void Hide()
    {
        _timer.Stop();
        AppWindow.Hide();
    }

    private void RestartTimer()
    {
        _timer.Stop();
        _timer.Start();
    }

    private void SetStatus(string glyph, string text)
    {
        StatusIcon.Glyph = glyph;
        StatusText.Text = text;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _timer.Stop();

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_busy) RestartTimer();
    }

    private async void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await ImageExport.CopyToClipboardAsync(_current.Image);
        SetStatus("", "Copied to clipboard");
    }

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            await CaptureActions.EditAsync(_current.Image);
            Hide();
        });
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            var path = await CaptureActions.SaveAsAsync(_current.Image, AppWindow.Id);
            if (path is not null)
                SetStatus("", $"Saved {Path.GetFileName(path)}");
            RestartTimer();
        });
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        PinRequested?.Invoke(_current);
        Hide();
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
            SetStatus("", ex.Message);
            RestartTimer();
        }
        finally
        {
            _busy = false;
        }
    }
}
