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
/// A capture pinned above other windows at its original screen position and size.
/// Drag to move, scroll to zoom (around the cursor), Ctrl+C to copy, Esc or double-click to close.
/// </summary>
public sealed partial class PinWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8;

    private readonly CapturedImage _image;
    private readonly DispatcherQueueTimer _badgeTimer;
    private double _zoom = 1;
    private PointInt32? _dragCursor;
    private PointInt32 _dragWindow;

    public PinWindow(CaptureResult capture)
    {
        _image = capture.Image;
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
        // Square corners: rounding would crop the pinned pixels.
        SetDwmInt(this.GetHwnd(), DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);

        AppWindow.MoveAndResize(capture.ScreenBounds);

        _badgeTimer = DispatcherQueue.CreateTimer();
        _badgeTimer.Interval = TimeSpan.FromSeconds(1.2);
        _badgeTimer.IsRepeating = false;
        _badgeTimer.Tick += (_, _) => ZoomBadge.Opacity = 0;
    }

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_image.ToSoftwareBitmap());
        Picture.Source = source;
        this.BringToFront();
    }

    // ---- Move ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        Root.CapturePointer(e.Pointer);
        _dragCursor = WindowHelper.CursorPosition();
        _dragWindow = AppWindow.Position;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragCursor is not { } start) return;
        var now = WindowHelper.CursorPosition();
        AppWindow.Move(new PointInt32(_dragWindow.X + now.X - start.X, _dragWindow.Y + now.Y - start.Y));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragCursor = null;
        Root.ReleasePointerCapture(e.Pointer);
    }

    // ---- Zoom ----

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        var factor = Math.Pow(1.1, delta / 120.0);
        SetZoom(_zoom * factor, WindowHelper.CursorPosition());
        e.Handled = true;
    }

    /// <summary>Resizes in physical pixels, keeping the image point under <paramref name="anchor"/> fixed.</summary>
    private void SetZoom(double zoom, PointInt32 anchor)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        var fx = size.Width > 0 ? (anchor.X - pos.X) / (double)size.Width : 0.5;
        var fy = size.Height > 0 ? (anchor.Y - pos.Y) / (double)size.Height : 0.5;

        var w = Math.Max(16, (int)Math.Round(_image.Width * zoom));
        var h = Math.Max(16, (int)Math.Round(_image.Height * zoom));
        AppWindow.MoveAndResize(new RectInt32(anchor.X - (int)(fx * w), anchor.Y - (int)(fy * h), w, h));
        _zoom = zoom;

        ZoomText.Text = $"{Math.Round(zoom * 100)}%";
        ZoomBadge.Opacity = 1;
        _badgeTimer.Stop();
        _badgeTimer.Start();
    }

    private void OnActualSize(object sender, RoutedEventArgs e)
    {
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        SetZoom(1, new PointInt32(pos.X + size.Width / 2, pos.Y + size.Height / 2));
    }

    // ---- Commands ----

    private async void OnCopy(object sender, RoutedEventArgs e) => await ImageExport.CopyToClipboardAsync(_image);

    private async void OnCopyAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ImageExport.CopyToClipboardAsync(_image);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Close();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }
}
