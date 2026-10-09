using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Views;

/// <summary>
/// Floating, always-on-top capture toolbar (design/mockups/Toolbar.html).
/// Reused: hidden rather than closed.
/// </summary>
public sealed partial class ToolbarWindow : Window
{
    /// <summary>Distance from the top of the work area to the panel (the mockup's top: 40px).</summary>
    private const double TopDip = 40;

    private readonly OverlappedPresenter _presenter;
    private bool _needsSize = true;

    /// <summary>New was pressed: capture in this mode after this many seconds.</summary>
    public event Action<CaptureMode, int>? CaptureRequested;

    public event Action? SettingsRequested;

    /// <summary>The HDR badge was clicked.</summary>
    public event Action? ColourRequested;

    /// <summary>The × button (or Esc); App decides whether that hides or exits.</summary>
    public event Action? CloseRequested;

    public ToolbarWindow()
    {
        InitializeComponent();
        ThemeService.Register(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Grip);

        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets/Capta.ico");
        TransparentBackdrop.PrepareWindow(this);

        // Never appear in our own (or anyone else's) captures.
        CaptureExclusion.Apply(this);

        // The shadow host tracks the panel exactly; the window padding leaves room for the blur.
        Panel.SizeChanged += (_, _) =>
        {
            ShadowHost.Width = Panel.ActualWidth;
            ShadowHost.Height = Panel.ActualHeight;
        };
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Shadow.Attach(ShadowHost, cornerRadius: 16, offsetY: 18, blur: 44, opacity: 0.5f);
        Interop.ClickThroughMargins.Attach(this, Panel);

        LoadSettings();

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    private void LoadSettings()
    {
        SelectMode(Settings.ToolbarMode);
        ApplyDelay(Settings.CaptureDelaySeconds);
        AutoCopyToggle.IsChecked = Settings.AutoCopy;
        AutoCopyCheck.Visibility = Settings.AutoCopy ? Visibility.Visible : Visibility.Collapsed;
        KeepOnTopToggle.IsChecked = Settings.ToolbarKeepOnTop;
        _presenter.IsAlwaysOnTop = Settings.ToolbarKeepOnTop;
    }

    public void ShowOnCursorMonitor()
    {
        var cursor = WindowHelper.CursorPosition();
        UpdateHdrBadge(cursor);
        ApplyDelay(Settings.CaptureDelaySeconds); // may have changed in the tray flyout
        ToolTipService.SetToolTip(RegionMode, $"Region ({Shortcuts.For(HotkeyAction.Region).Compact})");
        ToolTipService.SetToolTip(WindowMode, $"Window ({Shortcuts.For(HotkeyAction.ActiveWindow).Compact} captures the active window)");
        ToolTipService.SetToolTip(FullScreenMode, $"Full screen ({Shortcuts.For(HotkeyAction.FullScreen).Compact})");

        var area = WindowHelper.CursorWorkArea();
        // Move onto the target monitor first so the window adopts its DPI before sizing.
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();

        if (_needsSize)
        {
            Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            this.ResizeDip(Root.DesiredSize.Width, Root.DesiredSize.Height);
            _needsSize = false;
        }

        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (int)((TopDip - Root.Padding.Top) * scale)));
        this.BringToFront();
    }

    public void Hide() => AppWindow.Hide();

    // ---- HDR badge ----

    private void UpdateHdrBadge(PointInt32 cursor)
    {
        var monitors = Monitors.All().ToList();
        var index = monitors.FindIndex(m => m.Contains(cursor));
        var hdr = index >= 0 && GraphicsCaptureSource.IsHdr(monitors[index].Handle);
        var visibility = hdr ? Visibility.Visible : Visibility.Collapsed;
        if (HdrBadge.Visibility != visibility)
        {
            HdrBadge.Visibility = visibility;
            _needsSize = true;
        }
        HdrTitle.Text = $"Display {index + 1} is in HDR";
    }

    // ---- Mode group and New ----

    private ToggleButton[] ModeButtons => [RegionMode, WindowMode, FullScreenMode, FreeformMode];

    private void SelectMode(CaptureMode mode)
    {
        foreach (var b in ModeButtons)
            b.IsChecked = (string)b.Tag == mode.ToString();
    }

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        var mode = Enum.Parse<CaptureMode>((string)((ToggleButton)sender).Tag);
        Settings.ToolbarMode = mode;
        // Also re-checks the clicked button, so the active mode can't be toggled off.
        SelectMode(mode);
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        Hide();
        CaptureRequested?.Invoke(Settings.ToolbarMode, Settings.CaptureDelaySeconds);
    }

    // ---- Options ----

    private void ApplyDelay(int seconds)
    {
        DelayText.Text = seconds == 0 ? "Off" : $"{seconds}s";
        DelayOff.IsChecked = seconds == 0;
        Delay3.IsChecked = seconds == 3;
        Delay5.IsChecked = seconds == 5;
        Delay10.IsChecked = seconds == 10;
        _needsSize = true;
    }

    private void OnDelayClick(object sender, RoutedEventArgs e)
    {
        var seconds = int.Parse((string)((FrameworkElement)sender).Tag);
        Settings.CaptureDelaySeconds = seconds;
        ApplyDelay(seconds);
    }

    private void OnAutoCopyClick(object sender, RoutedEventArgs e)
    {
        var on = AutoCopyToggle.IsChecked == true;
        Settings.AutoCopy = on;
        AutoCopyCheck.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnKeepOnTopClick(object sender, RoutedEventArgs e)
    {
        var on = KeepOnTopToggle.IsChecked == true;
        Settings.ToolbarKeepOnTop = on;
        _presenter.IsAlwaysOnTop = on;
    }

    private void OnHide(object sender, RoutedEventArgs e) => Hide();

    private void OnClose(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void OnColour(object sender, RoutedEventArgs e)
    {
        Hide();
        ColourRequested?.Invoke();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        Hide();
        SettingsRequested?.Invoke();
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseRequested?.Invoke();
    }
}
