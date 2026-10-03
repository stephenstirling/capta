using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Views;

/// <summary>Floating, always-on-top mode picker. Reused: hidden rather than closed.</summary>
public sealed partial class ToolbarWindow : Window
{
    private const double TopMarginDip = 16;
    private bool _positioned;

    public event Action<CaptureMode>? ModeChosen;

    public ToolbarWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Grip);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets/Capta.ico");

        // Never appear in our own (or anyone else's) captures.
        SetWindowDisplayAffinity(this.GetHwnd(), WDA_EXCLUDEFROMCAPTURE);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void ShowOnCursorMonitor()
    {
        if (!_positioned)
        {
            // Size to content once, at the target monitor's DPI.
            Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            this.ResizeDip(Root.DesiredSize.Width, Root.DesiredSize.Height);
            _positioned = true;
        }

        var area = WindowHelper.CursorWorkArea();
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (int)(TopMarginDip * this.GetScale())));
        this.BringToFront();
    }

    public void Hide() => AppWindow.Hide();

    private void Choose(CaptureMode mode)
    {
        Hide();
        ModeChosen?.Invoke(mode);
    }

    private void OnRegion(object sender, RoutedEventArgs e) => Choose(CaptureMode.Region);
    private void OnWindow(object sender, RoutedEventArgs e) => Choose(CaptureMode.Window);
    private void OnFullScreen(object sender, RoutedEventArgs e) => Choose(CaptureMode.FullScreen);
    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }
}
