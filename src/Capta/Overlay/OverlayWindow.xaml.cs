using System.Runtime.InteropServices.WindowsRuntime;
using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using static Capta.Interop.NativeMethods;

namespace Capta.Overlay;

/// <summary>
/// Full-monitor selection surface. All geometry is tracked in physical pixels relative
/// to the monitor (matching the frozen frame) and converted to DIPs only for layout.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private const int LoupeCells = 15;
    private const int LoupeZoom = 8;
    private const int LoupeSize = LoupeCells * LoupeZoom;

    private readonly OverlaySession _session;
    private readonly MonitorInfo _monitor;
    private readonly CapturedImage _frame;
    private readonly CaptureMode _mode;
    private readonly IReadOnlyList<WindowTarget> _windows;
    private readonly WriteableBitmap _loupeBitmap = new(LoupeSize, LoupeSize);
    private readonly byte[] _loupePixels = new byte[LoupeSize * LoupeSize * 4];

    private PointInt32? _anchor;
    private RectInt32 _selection;
    private WindowTarget? _hoverWindow;

    public OverlayWindow(OverlaySession session, MonitorInfo monitor, CapturedImage frame, CaptureMode mode, IReadOnlyList<WindowTarget> windows)
    {
        _session = session;
        _monitor = monitor;
        _frame = frame;
        _mode = mode;
        _windows = windows;

        InitializeComponent();
        LoupeImage.Source = _loupeBitmap;
        HintText.Text = mode == CaptureMode.Window
            ? "Click a window to capture it  ·  Esc to cancel"
            : "Drag to select a region  ·  Esc to cancel";

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        var hwnd = this.GetHwnd();
        SetDwmInt(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);
        SetDwmInt(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND);
        AppWindow.MoveAndResize(monitor.Bounds);

        Root.SizeChanged += (_, _) => UpdateShade();
        Root.Cursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
    }

    public async Task PrepareAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToSoftwareBitmap());
        Backdrop.Source = source;
    }

    public void Show(bool focus)
    {
        Activate();
        // Moving onto a monitor with a different DPI can rescale the window; pin it again.
        AppWindow.MoveAndResize(_monitor.Bounds);
        if (focus)
        {
            this.BringToFront();
            FocusHost.Focus(FocusState.Programmatic);
        }
        UpdateShade();
    }

    private double Scale => Root.XamlRoot?.RasterizationScale ?? this.GetScale();

    private PointInt32 ToPixel(Point dip)
    {
        var s = Scale;
        return new PointInt32(
            Math.Clamp((int)Math.Floor(dip.X * s), 0, _frame.Width - 1),
            Math.Clamp((int)Math.Floor(dip.Y * s), 0, _frame.Height - 1));
    }

    private Rect ToDip(RectInt32 r)
    {
        var s = Scale;
        return new Rect(r.X / s, r.Y / s, r.Width / s, r.Height / s);
    }

    // ---- Input ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (point.Properties.IsRightButtonPressed)
        {
            _session.Cancel();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        if (_mode == CaptureMode.Window)
        {
            if (_hoverWindow is not null)
                _session.Complete(new OverlayResult.WindowSelected(_hoverWindow));
            return;
        }

        Root.CapturePointer(e.Pointer);
        _anchor = ToPixel(point.Position);
        _selection = new RectInt32(_anchor.Value.X, _anchor.Value.Y, 0, 0);
        UpdateSelectionVisuals();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var dip = e.GetCurrentPoint(Root).Position;
        var px = ToPixel(dip);
        _session.NotifyPointerOn(this);

        if (_mode == CaptureMode.Window)
        {
            var global = new PointInt32(px.X + _monitor.Bounds.X, px.Y + _monitor.Bounds.Y);
            var hit = WindowFinder.HitTest(_windows, global);
            if (hit != _hoverWindow)
            {
                _hoverWindow = hit;
                _selection = hit is null ? default : ToLocal(hit.Bounds);
                UpdateSelectionVisuals();
            }
            return;
        }

        if (_anchor is { } a)
        {
            int x0 = Math.Min(a.X, px.X), y0 = Math.Min(a.Y, px.Y);
            // Inclusive of the pixel under the cursor, so a 1px drag selects 1px.
            _selection = new RectInt32(x0, y0, Math.Abs(px.X - a.X) + 1, Math.Abs(px.Y - a.Y) + 1);
            UpdateSelectionVisuals();
        }
        UpdateLoupe(px, dip);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_mode != CaptureMode.Region || _anchor is null) return;
        Root.ReleasePointerCapture(e.Pointer);
        _anchor = null;

        if (_selection.Width >= 3 && _selection.Height >= 3)
        {
            _session.Complete(new OverlayResult.RegionSelected(_monitor, _frame, _selection));
        }
        else
        {
            // A click without a drag: reset and let the user try again.
            _selection = default;
            UpdateSelectionVisuals();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_anchor is null) Loupe.Visibility = Visibility.Collapsed;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            _session.Cancel();
        }
    }

    /// <summary>Called by the session when the pointer moves to another monitor's overlay.</summary>
    internal void OnPointerLeftMonitor()
    {
        if (_anchor is not null) return;
        Loupe.Visibility = Visibility.Collapsed;
        if (_mode == CaptureMode.Window && _hoverWindow is not null)
        {
            _hoverWindow = null;
            _selection = default;
            UpdateSelectionVisuals();
        }
    }

    private RectInt32 ToLocal(RectInt32 global)
    {
        // Clip to this monitor; the full window is still captured on selection.
        int x0 = Math.Max(global.X - _monitor.Bounds.X, 0);
        int y0 = Math.Max(global.Y - _monitor.Bounds.Y, 0);
        int x1 = Math.Min(global.X + global.Width - _monitor.Bounds.X, _frame.Width);
        int y1 = Math.Min(global.Y + global.Height - _monitor.Bounds.Y, _frame.Height);
        return new RectInt32(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    // ---- Visuals ----

    private void UpdateShade()
    {
        ShadeOuter.Rect = new Rect(0, 0, Root.ActualWidth, Root.ActualHeight);
        ShadeHole.Rect = _selection.Width > 0 ? ToDip(_selection) : Rect.Empty;
    }

    private void UpdateSelectionVisuals()
    {
        UpdateShade();
        if (_selection.Width <= 0 || _selection.Height <= 0)
        {
            SelectionFrame.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            Hint.Visibility = Visibility.Visible;
            return;
        }

        Hint.Visibility = Visibility.Collapsed;
        var r = ToDip(_selection);
        // Frame sits just outside the selection so it never covers captured pixels.
        Canvas.SetLeft(SelectionFrame, r.X - 1);
        Canvas.SetTop(SelectionFrame, r.Y - 1);
        SelectionFrame.Width = r.Width + 2;
        SelectionFrame.Height = r.Height + 2;
        SelectionFrame.Visibility = Visibility.Visible;

        SizeText.Text = $"{_selection.Width} × {_selection.Height}";
        SizeBadge.Visibility = Visibility.Visible;
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var badgeH = SizeBadge.DesiredSize.Height;
        Canvas.SetLeft(SizeBadge, Math.Max(4, r.X));
        Canvas.SetTop(SizeBadge, r.Y - badgeH - 6 >= 0 ? r.Y - badgeH - 6 : r.Y + 6);
    }

    private void UpdateLoupe(PointInt32 px, Point dip)
    {
        const int half = LoupeCells / 2;
        for (var cy = 0; cy < LoupeCells; cy++)
        for (var cx = 0; cx < LoupeCells; cx++)
        {
            var colour = _frame.GetPixel(px.X - half + cx, px.Y - half + cy) | 0xFF000000;
            for (var zy = 0; zy < LoupeZoom; zy++)
            {
                var row = ((cy * LoupeZoom + zy) * LoupeSize + cx * LoupeZoom) * 4;
                for (var zx = 0; zx < LoupeZoom; zx++)
                    BitConverter.TryWriteBytes(_loupePixels.AsSpan(row + zx * 4, 4), colour);
            }
        }
        using (var stream = _loupeBitmap.PixelBuffer.AsStream())
            stream.Write(_loupePixels);
        _loupeBitmap.Invalidate();

        var c = _frame.GetPixel(px.X, px.Y);
        LoupeText.Text = $"{px.X}, {px.Y}  #{(c >> 16) & 0xFF:X2}{(c >> 8) & 0xFF:X2}{c & 0xFF:X2}";

        Loupe.Visibility = Visibility.Visible;
        Loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Loupe.DesiredSize;
        const double offset = 24;
        var x = dip.X + offset + size.Width > Root.ActualWidth ? dip.X - offset - size.Width : dip.X + offset;
        var y = dip.Y + offset + size.Height > Root.ActualHeight ? dip.Y - offset - size.Height : dip.Y + offset;
        Canvas.SetLeft(Loupe, x);
        Canvas.SetTop(Loupe, y);
    }
}
