using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Views;

/// <summary>
/// A capture pinned above other windows at its original screen position and size
/// (design/mockups/Pinned.html). Drag to move, scroll to zoom around the cursor, drag the
/// corner mark to resize, Ctrl+C to copy, Esc or double-click to unpin. Hovering shows a
/// bar with opacity, click-through, copy, edit and unpin.
/// </summary>
public sealed partial class PinWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8;

    /// <summary>Stage extends this far past the image on each side, to hold the offset outline.</summary>
    private const double Inset = 4;

    private readonly CapturedImage _image;
    private readonly DispatcherQueueTimer _badgeTimer;
    private double _zoom = 1;
    private PointInt32 _imageOrigin;  // screen position of the pinned pixels (physical px)

    private enum Drag { None, Move, Resize }
    private Drag _drag;
    private PointInt32 _dragCursor;
    private PointInt32 _dragOrigin;
    private double _dragZoom;

    public bool IsClickThrough { get; private set; }

    /// <summary>Raised when click-through turns on or off, so the tray can offer a way back.</summary>
    public event Action<PinWindow>? ClickThroughChanged;

    public PinWindow(CaptureResult capture)
    {
        _image = capture.Image;
        _imageOrigin = new PointInt32(capture.ScreenBounds.X, capture.ScreenBounds.Y);
        InitializeComponent();
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

        Interop.Shadow.Attach(ShadowHost, cornerRadius: 10, offsetY: 24, blur: 60, opacity: 0.6);
        Bar.SizeChanged += (_, _) =>
        {
            BarShadow.Width = Bar.ActualWidth;
            BarShadow.Height = Bar.ActualHeight;
            ApplyLayout();
        };
        BarShadow.HorizontalAlignment = Bar.HorizontalAlignment = HorizontalAlignment.Left;
        BarShadow.VerticalAlignment = Bar.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(BarShadow, cornerRadius: 12, offsetY: 12, blur: 30, opacity: 0.5);

        _badgeTimer = DispatcherQueue.CreateTimer();
        _badgeTimer.Interval = TimeSpan.FromSeconds(1.2);
        _badgeTimer.IsRepeating = false;
        _badgeTimer.Tick += (_, _) => ZoomBadge.Opacity = 0;
    }

    public async Task ShowAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_image.ToSoftwareBitmap());
        Picture.Background = new ImageBrush { ImageSource = source, Stretch = Stretch.Fill };

        // Move onto the pin's monitor first so the window adopts its DPI before sizing.
        AppWindow.Move(_imageOrigin);
        ApplyLayout();
        this.BringToFront();
    }

    private double Scale => Root.XamlRoot?.RasterizationScale ?? this.GetScale();

    /// <summary>
    /// Sizes the window so the pinned pixels land at <see cref="_imageOrigin"/> at the current
    /// zoom, with room around them for the hover bar, hint and shadow.
    /// </summary>
    private void ApplyLayout()
    {
        var scale = Scale;
        double imageW = Math.Max(16, _image.Width * _zoom) / scale;
        double imageH = Math.Max(16, _image.Height * _zoom) / scale;
        Stage.Width = imageW + 2 * Inset;
        Stage.Height = imageH + 2 * Inset;

        // Wide enough for the hover bar even when the pin is small; the bar and hint stay
        // right-aligned with the pin's right edge.
        var barW = Bar.ActualWidth > 0 ? Bar.ActualWidth : 360;
        var contentW = Math.Max(imageW, barW);
        var extraLeft = contentW - imageW;
        Stage.Margin = new Thickness(40 - Inset + extraLeft, 60 - Inset, 40 - Inset, 60 - Inset);
        BarLayer.Margin = new Thickness(0, 6, 40, 0);
        Hint.Margin = new Thickness(0, 0, 40, 36);

        var clientW = contentW + 80;
        var clientH = imageH + 120;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(clientW * scale), (int)Math.Ceiling(clientH * scale)));
        AppWindow.Move(new PointInt32(
            _imageOrigin.X - (int)Math.Round((40 + extraLeft) * scale),
            _imageOrigin.Y - (int)Math.Round(60 * scale)));
    }

    // ---- Hover bar ----

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        BarLayer.Visibility = Visibility.Visible;
        Hint.Visibility = Visibility.Visible;
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_drag != Drag.None) return;
        BarLayer.Visibility = Visibility.Collapsed;
        Hint.Visibility = Visibility.Collapsed;
    }

    private void OnOpacityChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (OpacityText is null) return; // raised once during InitializeComponent
        Stage.Opacity = e.NewValue / 100;
        OpacityText.Text = $"{e.NewValue:0}%";
    }

    /// <summary>Lets clicks pass through to the windows underneath (WS_EX_LAYERED | WS_EX_TRANSPARENT).</summary>
    public void SetClickThrough(bool on)
    {
        var hwnd = this.GetHwnd();
        var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        style = on ? style | WS_EX_LAYERED | WS_EX_TRANSPARENT : style & ~(long)WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)style);
        if (on) SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        IsClickThrough = on;
        if (on)
        {
            BarLayer.Visibility = Visibility.Collapsed;
            Hint.Visibility = Visibility.Collapsed;
        }
        ClickThroughChanged?.Invoke(this);
    }

    private void OnClickThrough(object sender, RoutedEventArgs e) => SetClickThrough(true);

    // ---- Move and resize ----

    private void OnStagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Stage).Properties.IsLeftButtonPressed) return;
        Stage.CapturePointer(e.Pointer);
        _dragCursor = WindowHelper.CursorPosition();
        _dragOrigin = _imageOrigin;
        _dragZoom = _zoom;
        var p = e.GetCurrentPoint(ResizeGrip).Position;
        _drag = p.X >= 0 && p.Y >= 0 && p.X <= ResizeGrip.ActualWidth && p.Y <= ResizeGrip.ActualHeight ? Drag.Resize : Drag.Move;
    }

    private void OnStagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == Drag.None) return;
        var now = WindowHelper.CursorPosition();
        int dx = now.X - _dragCursor.X, dy = now.Y - _dragCursor.Y;
        if (_drag == Drag.Move)
        {
            _imageOrigin = new PointInt32(_dragOrigin.X + dx, _dragOrigin.Y + dy);
            ApplyLayout();
        }
        else
        {
            // Keep the aspect ratio: follow whichever axis moved further.
            var fromX = (_image.Width * _dragZoom + dx) / _image.Width;
            var fromY = (_image.Height * _dragZoom + dy) / _image.Height;
            SetZoom(Math.Abs(dx) >= Math.Abs(dy) ? fromX : fromY, anchor: null);
        }
    }

    private void OnStagePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _drag = Drag.None;
        Stage.ReleasePointerCapture(e.Pointer);
    }

    // ---- Zoom ----

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(Stage).Properties.MouseWheelDelta;
        SetZoom(_zoom * Math.Pow(1.1, delta / 120.0), WindowHelper.CursorPosition());
        e.Handled = true;
    }

    /// <summary>Sets the zoom, keeping the image point under <paramref name="anchor"/> fixed (or the top-left).</summary>
    private void SetZoom(double zoom, PointInt32? anchor)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (anchor is { } a)
        {
            var fx = (a.X - _imageOrigin.X) / (_image.Width * _zoom);
            var fy = (a.Y - _imageOrigin.Y) / (_image.Height * _zoom);
            _imageOrigin = new PointInt32(
                a.X - (int)Math.Round(fx * _image.Width * zoom),
                a.Y - (int)Math.Round(fy * _image.Height * zoom));
        }
        _zoom = zoom;
        ApplyLayout();

        ZoomText.Text = $"{Math.Round(zoom * 100)}%";
        ZoomBadge.Opacity = 1;
        _badgeTimer.Stop();
        _badgeTimer.Start();
    }

    private void OnActualSize(object sender, RoutedEventArgs e) => SetZoom(1, anchor: null);

    // ---- Commands ----

    private async void OnCopy(object sender, RoutedEventArgs e) => await ImageExport.CopyToClipboardAsync(_image);

    private async void OnCopyAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ImageExport.CopyToClipboardAsync(_image);
    }

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        try
        {
            await CaptureActions.EditAsync(_image);
        }
        catch (Exception ex)
        {
            Log.Error("Pin edit failed", ex);
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Close();

    private void OnUnpin(object sender, RoutedEventArgs e) => Close();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }
}
