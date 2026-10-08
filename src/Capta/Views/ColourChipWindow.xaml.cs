using Capta.Interop;
using Capta.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace Capta.Views;

/// <summary>
/// A colour picked in the overlay (press C), pinned above other windows: swatch, hex code and
/// an unpin button. Drag to move; click the code to copy it; double-click or Esc to unpin.
/// </summary>
public sealed partial class ColourChipWindow : Window
{
    private readonly Color _colour;
    private bool _dragging;
    private PointInt32 _dragCursor;
    private PointInt32 _dragOrigin;

    public ColourChipWindow(Color colour)
    {
        _colour = colour;
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

        Interop.Shadow.Attach(ShadowHost, cornerRadius: 12, offsetY: 12, blur: 30, opacity: 0.5);
        Interop.ClickThroughMargins.Attach(this, Panel);

        Swatch.Background = new SolidColorBrush(colour);
        HexText.Text = Hex;
    }

    public string Hex => $"#{_colour.R:X2}{_colour.G:X2}{_colour.B:X2}";

    private string Rgb => $"rgb({_colour.R}, {_colour.G}, {_colour.B})";

    /// <summary>Shows the chip just below and right of <paramref name="screenPoint"/>, kept on its monitor.</summary>
    public void ShowAt(PointInt32 screenPoint)
    {
        AppWindow.Move(screenPoint); // adopt the monitor's DPI before sizing
        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = Root.DesiredSize;
        this.ResizeDip(desired.Width, desired.Height);

        var scale = this.GetScale();
        var pad = Root.Padding;
        var size = AppWindow.Size;
        var area = DisplayArea.GetFromPoint(screenPoint, DisplayAreaFallback.Nearest).WorkArea;
        const double gap = 14; // keep the picked pixel visible
        var x = screenPoint.X + (int)((gap - pad.Left) * scale);
        var y = screenPoint.Y + (int)((gap - pad.Top) * scale);
        x = Math.Clamp(x, area.X - (int)(pad.Left * scale), area.X + area.Width - size.Width + (int)(pad.Right * scale));
        y = Math.Clamp(y, area.Y - (int)(pad.Top * scale), area.Y + area.Height - size.Height + (int)(pad.Bottom * scale));
        AppWindow.Move(new PointInt32(x, y));
        this.BringToFront();
        // Focus inside the chip so Esc and Ctrl+C work, without a keyboard focus ring (it was
        // opened by a key press, which would otherwise show one).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => HexButton.Focus(FocusState.Pointer)); // after activation's own initial focus
    }

    private void Copy(string text, string status)
    {
        CaptureActions.CopyText(text);
        ToolTipService.SetToolTip(HexButton, status);
    }

    private void OnCopyHex(object sender, RoutedEventArgs e) => Copy(Hex, "Copied");

    private void OnCopyRgb(object sender, RoutedEventArgs e) => Copy(Rgb, "Copied RGB");

    private void OnCopyAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        Copy(Hex, "Copied");
        e.Handled = true;
    }

    private void OnUnpin(object sender, RoutedEventArgs e) => Close();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e) => Close();

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Close();

    // ---- Drag to move ----

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Panel).Properties.IsLeftButtonPressed) return;
        _dragging = Panel.CapturePointer(e.Pointer);
        _dragCursor = WindowHelper.CursorPosition();
        _dragOrigin = AppWindow.Position;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var cursor = WindowHelper.CursorPosition();
        AppWindow.Move(new PointInt32(_dragOrigin.X + cursor.X - _dragCursor.X, _dragOrigin.Y + cursor.Y - _dragCursor.Y));
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Panel.ReleasePointerCapture(e.Pointer);
    }
}
