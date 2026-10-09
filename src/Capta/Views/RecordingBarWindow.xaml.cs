using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// The bar shown while recording: a blinking dot and the elapsed time, toggles for system audio
/// and the microphone, and Stop. It sits just outside the recorded area and is excluded from
/// captures, so it never appears in the video.
/// </summary>
public sealed partial class RecordingBarWindow : Window
{
    private readonly DispatcherQueueTimer _timer;
    private Func<TimeSpan>? _elapsed;
    private bool _dragging;
    private PointInt32 _dragCursor;
    private PointInt32 _dragOrigin;

    public event Action? StopRequested;

    /// <summary>The user switched a source; the handler returns whether it's actually on now.</summary>
    public Func<bool, Task<bool>>? SystemAudioChanged { get; set; }

    public Func<bool, Task<bool>>? MicrophoneChanged { get; set; }

    public RecordingBarWindow()
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

        Interop.Shadow.Attach(ShadowHost, cornerRadius: 16, offsetY: 18, blur: 44, opacity: 0.5);
        Interop.ClickThroughMargins.Attach(this, Panel);

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Shows the bar centred above the recorded area (or below it, or inside its top edge).</summary>
    public void Show(RectInt32 recorded, MonitorInfo monitor, Func<TimeSpan> elapsed, bool systemAudio, bool microphone)
    {
        _elapsed = elapsed;
        SystemAudioToggle.IsChecked = systemAudio;
        MicToggle.IsChecked = microphone;
        ToolTipService.SetToolTip(StopButton, $"Stop recording ({Shortcuts.For(HotkeyAction.Record).Compact})");
        Tick();

        var area = monitor.WorkArea;
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2)); // adopt the monitor's DPI
        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        this.ResizeDip(Root.DesiredSize.Width, Root.DesiredSize.Height);

        var scale = this.GetScale();
        var pad = Root.Padding;
        var size = AppWindow.Size;
        int panelH = (int)((Root.DesiredSize.Height - pad.Top - pad.Bottom) * scale);
        int gap = (int)(10 * scale);
        int x = recorded.X + recorded.Width / 2 - size.Width / 2;
        int panelTop;
        if (recorded.Y - area.Y >= panelH + gap)
            panelTop = recorded.Y - gap - panelH;                       // above
        else if (area.Y + area.Height - (recorded.Y + recorded.Height) >= panelH + gap)
            panelTop = recorded.Y + recorded.Height + gap;              // below
        else
            panelTop = recorded.Y + gap;                                // inside (full screen); excluded from the video
        int y = panelTop - (int)(pad.Top * scale);
        x = Math.Clamp(x, area.X - (int)(pad.Left * scale), area.X + area.Width - size.Width + (int)(pad.Right * scale));
        AppWindow.Move(new PointInt32(x, y));

        _timer.Start();
        AppWindow.Show(activateWindow: false);
    }

    public void Hide()
    {
        _timer.Stop();
        AppWindow.Hide();
    }

    private void Tick()
    {
        var t = _elapsed?.Invoke() ?? TimeSpan.Zero;
        TimeText.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        Dot.Opacity = t.Milliseconds < 500 ? 1 : 0.35;
    }

    private async void OnSystemAudio(object sender, RoutedEventArgs e)
    {
        var want = SystemAudioToggle.IsChecked == true;
        if (SystemAudioChanged is not null) SystemAudioToggle.IsChecked = await SystemAudioChanged(want);
    }

    private async void OnMicrophone(object sender, RoutedEventArgs e)
    {
        var want = MicToggle.IsChecked == true;
        if (MicrophoneChanged is not null) MicToggle.IsChecked = await MicrophoneChanged(want);
    }

    private void OnStop(object sender, RoutedEventArgs e) => StopRequested?.Invoke();

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
