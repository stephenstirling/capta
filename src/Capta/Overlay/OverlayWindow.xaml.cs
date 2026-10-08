using System.Runtime.InteropServices.WindowsRuntime;
using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using Stirling.Shared;
using static Capta.Interop.NativeMethods;

namespace Capta.Overlay;

/// <summary>
/// Full-monitor selection surface (design/mockups/Main.html). Geometry is tracked in
/// physical pixels relative to the monitor (matching the frozen frame) and converted
/// to DIPs only for layout.
/// </summary>
/// <remarks>
/// Region mode: drag to draw; afterwards the selection stays editable (handles resize,
/// dragging inside moves). Space moves while drawing, Shift keeps it square, Enter or a
/// double-click captures. Window mode: click the highlighted window.
/// </remarks>
public sealed partial class OverlayWindow : Window
{
    private const int LoupeCells = 11;
    private const int LoupeZoom = 12;
    private const int LoupeSize = LoupeCells * LoupeZoom; // 132, the mockup's loupe
    private const double HandleHitDip = 10;

    private enum Drag { None, New, Move, Resize }

    private readonly OverlaySession _session;
    private readonly MonitorInfo _monitor;
    private readonly CapturedImage _frame;
    private readonly IReadOnlyList<WindowTarget> _windows;
    private readonly WriteableBitmap _loupeBitmap = new(LoupeSize, LoupeSize);
    private readonly byte[] _loupePixels = new byte[LoupeSize * LoupeSize * 4];
    private readonly Border[] _handles;

    private CaptureMode _mode;

    /// <summary>Region and Grab text both draw an editable selection.</summary>
    private bool IsRegionLike => _mode is CaptureMode.Region or CaptureMode.GrabText;
    private RectInt32 _selection;
    private WindowTarget? _hoverWindow;

    private Drag _drag;
    private int _resizeHandle;
    private PointInt32 _dragStart;
    private PointInt32 _lastPoint;
    private RectInt32 _dragStartSelection;
    private bool _spaceDown;

    public OverlayWindow(OverlaySession session, MonitorInfo monitor, CapturedImage frame, CaptureMode mode, IReadOnlyList<WindowTarget> windows)
    {
        _session = session;
        _monitor = monitor;
        _frame = frame;
        _windows = windows;

        InitializeComponent();
        _handles = [HandleNW, HandleN, HandleNE, HandleE, HandleSE, HandleS, HandleSW, HandleW];
        LoupeImage.Fill = new ImageBrush { ImageSource = _loupeBitmap, Stretch = Stretch.Fill };

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

        ModeBar.SizeChanged += (_, _) =>
        {
            ModeBarShadow.Width = ModeBar.ActualWidth;
            ModeBarShadow.Height = ModeBar.ActualHeight;
        };
        Interop.Shadow.Attach(ModeBarShadow, cornerRadius: 16, offsetY: 16, blur: 40, opacity: 0.45);
        Interop.Shadow.Attach(LoupeShadow, cornerRadius: 66, offsetY: 12, blur: 30, opacity: 0.5);

        Root.SizeChanged += (_, _) => UpdateSelectionVisuals();
        Root.Cursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        ApplyMode(mode);
    }

    public async Task PrepareAsync()
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(_frame.ToSoftwareBitmap());
        Backdrop.Source = source;
    }

    public void Show(bool focus)
    {
        this.ShowRestored();
        // Moving onto a monitor with a different DPI can rescale the window; pin it again.
        AppWindow.MoveAndResize(_monitor.Bounds);
        if (focus)
        {
            this.BringToFront();
            FocusHost.Focus(FocusState.Programmatic);
            Log.Info($"Overlay shown; foreground is ours: {GetForegroundWindow() == this.GetHwnd()}");
        }
        UpdateSelectionVisuals();
    }

    /// <summary>Switches Region/Window; called by the session on every monitor's overlay.</summary>
    internal void ApplyMode(CaptureMode mode)
    {
        _mode = mode;
        RegionMode.IsChecked = mode == CaptureMode.Region;
        WindowMode.IsChecked = mode == CaptureMode.Window;
        GrabTextMode.IsChecked = mode == CaptureMode.GrabText;
        HintLead.Text = mode switch
        {
            CaptureMode.Window => "Click a window",
            CaptureMode.GrabText => "Select the text to copy",
            _ => "Drag to select",
        };
        RegionHints.Visibility = mode == CaptureMode.Window ? Visibility.Collapsed : Visibility.Visible;
        _selection = default;
        _hoverWindow = null;
        _drag = Drag.None;
        UpdateSelectionVisuals();
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

    private bool HasSelection => _selection.Width > 0 && _selection.Height > 0;

    // ---- Pointer ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (point.Properties.IsRightButtonPressed)
        {
            _session.Cancel();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;
        FocusHost.Focus(FocusState.Programmatic);

        if (_mode == CaptureMode.Window)
        {
            if (_hoverWindow is not null)
                _session.Complete(new OverlayResult.WindowSelected(_hoverWindow));
            return;
        }

        var px = ToPixel(point.Position);
        Root.CapturePointer(e.Pointer);
        _dragStart = _lastPoint = px;
        _dragStartSelection = _selection;

        var handle = HasSelection ? HitHandle(point.Position) : -1;
        if (handle >= 0)
        {
            _drag = Drag.Resize;
            _resizeHandle = handle;
        }
        else if (HasSelection && Contains(_selection, px))
        {
            _drag = Drag.Move;
        }
        else
        {
            _drag = Drag.New;
            _selection = new RectInt32(px.X, px.Y, 1, 1);
            UpdateSelectionVisuals();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var dip = e.GetCurrentPoint(Root).Position;
        var px = ToPixel(dip);
        _session.NotifyPointerOn(this);

        if (_drag == Drag.None && IsOverTopBar(dip))
        {
            Loupe.Visibility = Visibility.Collapsed;
            SetCursor(InputSystemCursorShape.Arrow);
            return;
        }

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
            SetCursor(InputSystemCursorShape.Cross);
            Loupe.Visibility = Visibility.Collapsed;
            return;
        }

        switch (_drag)
        {
            case Drag.New:
                if (_spaceDown)
                {
                    // Space: move the selection being drawn instead of resizing it.
                    _dragStart = new PointInt32(_dragStart.X + px.X - _lastPoint.X, _dragStart.Y + px.Y - _lastPoint.Y);
                }
                _selection = FromCorners(_dragStart, px, square: IsShiftDown());
                break;
            case Drag.Move:
                _selection = Offset(_dragStartSelection, px.X - _dragStart.X, px.Y - _dragStart.Y);
                break;
            case Drag.Resize:
                _selection = Resize(_dragStartSelection, _resizeHandle, px.X - _dragStart.X, px.Y - _dragStart.Y, IsShiftDown());
                break;
            default:
                UpdateHoverCursor(dip, px);
                break;
        }
        _lastPoint = px;

        if (_drag != Drag.None)
            UpdateSelectionVisuals();
        UpdateLoupe(px, dip);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == Drag.None) return;
        Root.ReleasePointerCapture(e.Pointer);
        var wasNew = _drag == Drag.New;
        _drag = Drag.None;

        // A click without a drag clears the selection rather than leaving a 1px one.
        if (wasNew && (_selection.Width < 3 || _selection.Height < 3))
            _selection = default;
        UpdateSelectionVisuals();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == Drag.None) Loupe.Visibility = Visibility.Collapsed;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (IsRegionLike && HasSelection && Contains(_selection, ToPixel(e.GetPosition(Root))))
            CompleteSelection();
    }

    // ---- Keyboard ----

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                _session.Cancel();
                break;
            case VirtualKey.Enter when IsRegionLike && HasSelection:
                CompleteSelection();
                break;
            case VirtualKey.Space:
                _spaceDown = true;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space)
        {
            _spaceDown = false;
            e.Handled = true;
        }
    }

    private static bool IsShiftDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    // ---- Mode bar ----

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        var mode = Enum.Parse<CaptureMode>((string)((ToggleButton)sender).Tag);
        _session.SetMode(mode);
        FocusHost.Focus(FocusState.Programmatic);
    }

    private void OnFullScreenClick(object sender, RoutedEventArgs e) =>
        _session.Complete(new OverlayResult.RegionSelected(_monitor, _frame,
            new RectInt32(0, 0, _frame.Width, _frame.Height), CaptureMode.FullScreen));

    private void OnCancelClick(object sender, RoutedEventArgs e) => _session.Cancel();

    private bool IsOverTopBar(Point dip)
    {
        var bounds = TopBar.TransformToVisual(Root).TransformBounds(new Rect(0, 0, TopBar.ActualWidth, TopBar.ActualHeight));
        return bounds.Contains(dip);
    }

    private void CompleteSelection() =>
        _session.Complete(new OverlayResult.RegionSelected(_monitor, _frame, _selection, _mode));

    /// <summary>Called by the session when the pointer moves to another monitor's overlay.</summary>
    internal void OnPointerLeftMonitor()
    {
        if (_drag != Drag.None) return;
        Loupe.Visibility = Visibility.Collapsed;
        if (_mode == CaptureMode.Window && _hoverWindow is not null)
        {
            _hoverWindow = null;
            _selection = default;
            UpdateSelectionVisuals();
        }
    }

    // ---- Selection geometry (physical pixels) ----

    private static bool Contains(RectInt32 r, PointInt32 p) =>
        p.X >= r.X && p.Y >= r.Y && p.X < r.X + r.Width && p.Y < r.Y + r.Height;

    /// <summary>Rectangle spanning two pixels, inclusive, optionally constrained to a square.</summary>
    private RectInt32 FromCorners(PointInt32 a, PointInt32 b, bool square)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        if (square)
        {
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = Math.Sign(dx == 0 ? 1 : dx) * side;
            dy = Math.Sign(dy == 0 ? 1 : dy) * side;
        }
        int x0 = Math.Min(a.X, a.X + dx), y0 = Math.Min(a.Y, a.Y + dy);
        return ClampToFrame(new RectInt32(x0, y0, Math.Abs(dx) + 1, Math.Abs(dy) + 1));
    }

    private RectInt32 Offset(RectInt32 r, int dx, int dy) => new(
        Math.Clamp(r.X + dx, 0, _frame.Width - r.Width),
        Math.Clamp(r.Y + dy, 0, _frame.Height - r.Height),
        r.Width, r.Height);

    /// <summary>Moves the edges a handle controls (0=NW, then clockwise to 7=W).</summary>
    private RectInt32 Resize(RectInt32 r, int handle, int dx, int dy, bool square)
    {
        int left = r.X, top = r.Y, right = r.X + r.Width, bottom = r.Y + r.Height;
        var movesLeft = handle is 0 or 6 or 7;
        var movesRight = handle is 2 or 3 or 4;
        var movesTop = handle is 0 or 1 or 2;
        var movesBottom = handle is 4 or 5 or 6;
        if (movesLeft) left += dx;
        if (movesRight) right += dx;
        if (movesTop) top += dy;
        if (movesBottom) bottom += dy;

        if (square && handle is 0 or 2 or 4 or 6)
        {
            var side = Math.Max(Math.Abs(right - left), Math.Abs(bottom - top));
            if (movesLeft) left = right - side; else right = left + side;
            if (movesTop) top = bottom - side; else bottom = top + side;
        }

        // Dragging past the opposite edge flips the rectangle rather than inverting it.
        int x0 = Math.Min(left, right), x1 = Math.Max(left, right);
        int y0 = Math.Min(top, bottom), y1 = Math.Max(top, bottom);
        return ClampToFrame(new RectInt32(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)));
    }

    private RectInt32 ClampToFrame(RectInt32 r)
    {
        int x0 = Math.Clamp(r.X, 0, _frame.Width), y0 = Math.Clamp(r.Y, 0, _frame.Height);
        int x1 = Math.Clamp(r.X + r.Width, 0, _frame.Width), y1 = Math.Clamp(r.Y + r.Height, 0, _frame.Height);
        return new RectInt32(x0, y0, x1 - x0, y1 - y0);
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

    /// <summary>Handle centres in DIPs, in handle order (NW, N, NE, E, SE, S, SW, W).</summary>
    private Point[] HandleCentres()
    {
        var r = ToDip(_selection);
        double l = r.X, t = r.Y, rt = r.X + r.Width, b = r.Y + r.Height, cx = l + r.Width / 2, cy = t + r.Height / 2;
        return [new(l, t), new(cx, t), new(rt, t), new(rt, cy), new(rt, b), new(cx, b), new(l, b), new(l, cy)];
    }

    private int HitHandle(Point dip)
    {
        var centres = HandleCentres();
        for (var i = 0; i < centres.Length; i++)
            if (Math.Abs(dip.X - centres[i].X) <= HandleHitDip && Math.Abs(dip.Y - centres[i].Y) <= HandleHitDip)
                return i;
        return -1;
    }

    private void UpdateHoverCursor(Point dip, PointInt32 px)
    {
        var handle = HasSelection ? HitHandle(dip) : -1;
        SetCursor(handle switch
        {
            0 or 4 => InputSystemCursorShape.SizeNorthwestSoutheast,
            2 or 6 => InputSystemCursorShape.SizeNortheastSouthwest,
            1 or 5 => InputSystemCursorShape.SizeNorthSouth,
            3 or 7 => InputSystemCursorShape.SizeWestEast,
            _ when HasSelection && Contains(_selection, px) => InputSystemCursorShape.SizeAll,
            _ => InputSystemCursorShape.Cross,
        });
    }

    private InputSystemCursorShape _cursorShape = InputSystemCursorShape.Cross;

    private void SetCursor(InputSystemCursorShape shape)
    {
        if (shape == _cursorShape) return;
        _cursorShape = shape;
        Root.Cursor = InputSystemCursor.Create(shape);
    }

    // ---- Visuals ----

    private void UpdateSelectionVisuals()
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, Root.ActualWidth, Root.ActualHeight) });
        if (HasSelection)
            group.Children.Add(new RectangleGeometry { Rect = ToDip(_selection) });
        Shade.Data = group;

        if (!HasSelection)
        {
            SelectionLayer.Visibility = Visibility.Collapsed;
            return;
        }
        SelectionLayer.Visibility = Visibility.Visible;

        // The 2px frame sits inside the selection (box-sizing: border-box in the mockup).
        var r = ToDip(_selection);
        Place(SelectionFrame, r.X, r.Y, r.Width, r.Height);

        Place(GuideV1, r.X + r.Width / 3, r.Y, 1, r.Height);
        Place(GuideV2, r.X + r.Width * 2 / 3, r.Y, 1, r.Height);
        Place(GuideH1, r.X, r.Y + r.Height / 3, r.Width, 1);
        Place(GuideH2, r.X, r.Y + r.Height * 2 / 3, r.Width, 1);

        // Handles only when the selection can be edited (Region mode).
        var showHandles = IsRegionLike;
        var centres = HandleCentres();
        for (var i = 0; i < _handles.Length; i++)
        {
            _handles[i].Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(_handles[i], centres[i].X - 7);
            Canvas.SetTop(_handles[i], centres[i].Y - 7);
        }
        foreach (var g in new[] { GuideV1, GuideV2, GuideH1, GuideH2 })
            g.Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;

        // Amber size badge 40px above the selection (inside it near the top of the screen).
        SizeText.Text = $"{_selection.Width} × {_selection.Height} px";
        Canvas.SetLeft(SizeBadge, Math.Max(4, r.X - 2));
        Canvas.SetTop(SizeBadge, r.Y >= 40 ? r.Y - 40 : r.Y + 10);
    }

    private static void Place(FrameworkElement e, double x, double y, double w, double h)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        e.Width = Math.Max(0, w);
        e.Height = Math.Max(0, h);
    }

    private void UpdateLoupe(PointInt32 px, Point dip)
    {
        const int half = LoupeCells / 2;
        for (var cy = 0; cy < LoupeCells; cy++)
        for (var cx = 0; cx < LoupeCells; cx++)
        {
            var colour = DisplayedPixel(px.X - half + cx, px.Y - half + cy);
            for (var zy = 0; zy < LoupeZoom; zy++)
            {
                var row = ((cy * LoupeZoom + zy) * LoupeSize + cx * LoupeZoom) * 4;
                for (var zx = 0; zx < LoupeZoom; zx++)
                {
                    // 1px grid line at the top/left of each cell, 9% white (mockup's background grid).
                    var c = zx == 0 || zy == 0 ? Lighten(colour, 0.09) : colour;
                    BitConverter.TryWriteBytes(_loupePixels.AsSpan(row + zx * 4, 4), c);
                }
            }
        }
        using (var stream = _loupeBitmap.PixelBuffer.AsStream())
            stream.Write(_loupePixels);
        _loupeBitmap.Invalidate();

        var pixel = _frame.GetPixel(px.X, px.Y);
        byte r = (byte)(pixel >> 16), g = (byte)(pixel >> 8), b = (byte)pixel;
        CoordText.Text = $"{px.X}, {px.Y}";
        HexText.Text = $"#{r:X2}{g:X2}{b:X2}";
        Swatch.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, r, g, b));

        Loupe.Visibility = Visibility.Visible;
        Loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Loupe.DesiredSize;
        const double offsetX = 20, offsetY = 16;
        var x = dip.X + offsetX + size.Width > Root.ActualWidth ? dip.X - offsetX - size.Width : dip.X + offsetX;
        var y = dip.Y + offsetY + size.Height > Root.ActualHeight ? dip.Y - offsetY - size.Height : dip.Y + offsetY;
        Canvas.SetLeft(Loupe, x);
        Canvas.SetTop(Loupe, y);
    }

    /// <summary>
    /// The pixel as the overlay shows it: dimmed by the scrim outside the selection, amber on
    /// the selection frame. The loupe magnifies what's on screen, as in the mockup.
    /// </summary>
    private uint DisplayedPixel(int x, int y)
    {
        var pixel = _frame.GetPixel(x, y) | 0xFF000000;
        if (!HasSelection) return Blend(pixel, StirlingColors.Scrim);

        var s = _selection;
        if (!Contains(s, new PointInt32(x, y))) return Blend(pixel, StirlingColors.Scrim);
        if (IsRegionLike || _hoverWindow is not null)
        {
            // One magnified pixel of frame, as in screenshots/03 (a full 2px band reads too heavy at 12×).
            if (x == s.X || y == s.Y || x == s.X + s.Width - 1 || y == s.Y + s.Height - 1)
                return ToBgra(StirlingColors.Accent);
        }
        return pixel;
    }

    private static uint ToBgra(Windows.UI.Color c) => (uint)(0xFF << 24 | c.R << 16 | c.G << 8 | c.B);

    /// <summary>Source-over blend of a translucent colour onto an opaque BGRA pixel.</summary>
    private static uint Blend(uint bgra, Windows.UI.Color over)
    {
        var a = over.A / 255.0;
        uint Channel(int shift, byte top) => (uint)Math.Round(((bgra >> shift) & 0xFF) * (1 - a) + top * a) << shift;
        return 0xFF000000 | Channel(16, over.R) | Channel(8, over.G) | Channel(0, over.B);
    }

    private static uint Lighten(uint bgra, double amount)
    {
        uint Channel(int shift)
        {
            var v = (bgra >> shift) & 0xFF;
            return (uint)(v + (255 - v) * amount) << shift;
        }
        return 0xFF000000 | Channel(16) | Channel(8) | Channel(0);
    }
}
