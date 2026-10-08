using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Capta.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;

namespace Capta;

public partial class App : Application
{
    private const string NoticeShownKey = "PrintScreenNotice.Shown";

    private TrayIconService? _tray;
    private PrintScreenHook? _hook;
    private PrintScreenOwnership? _ownership;
    private PrintScreenNoticeWindow? _notice;
    private ToolbarWindow? _toolbar;
    private CaptureCoordinator? _capture;
    private readonly GraphicsCaptureSource _screenSource = new();
    private CaptureCardWindow? _card;
    private readonly List<PinWindow> _pins = [];

    public static new App? Current => (App?)Application.Current;

    public DispatcherQueue Dispatcher { get; } = DispatcherQueue.GetForCurrentThread();

    public App()
    {
        InitializeComponent();
        // No main window: the process lives in the tray until the user chooses Exit.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Start hidden, whether launched by the startup task or from Start.
        _capture = new CaptureCoordinator(_screenSource);
        _capture.Captured += OnCaptured;

        _tray = new TrayIconService(this);
        _tray.Create();

        _hook = new PrintScreenHook(Dispatcher);
        _hook.Pressed += OnHotkey;
        _hook.Install();
        Log.Info("Tray icon and Print Screen hook installed");

        _ownership = new PrintScreenOwnership();
        _ownership.Changed += owned => Dispatcher.TryEnqueue(() => OnPrintScreenOwnershipChanged(owned));
        _ownership.StartWatching();
        Log.Info($"Windows owns Print Screen: {_ownership.WindowsOwnsKey}");
        OnPrintScreenOwnershipChanged(_ownership.WindowsOwnsKey);

        // Pop the explanation once; afterwards the tray menu carries the warning.
        var settings = ApplicationData.Current.LocalSettings.Values;
        if (_ownership.WindowsOwnsKey && !settings.ContainsKey(NoticeShownKey))
        {
            settings[NoticeShownKey] = true;
            ShowPrintScreenNotice();
        }

        await StartupTaskService.EnsureEnabledOnFirstRunAsync();
        await _tray.RefreshStartupStateAsync();
    }

    private void OnHotkey(HotkeyAction action)
    {
        Log.Info($"Hotkey: {action}");
        switch (action)
        {
            case HotkeyAction.Region: StartCapture(CaptureMode.Region); break;
            case HotkeyAction.FullScreen: StartCapture(CaptureMode.FullScreen); break;
            case HotkeyAction.ActiveWindow: StartCapture(CaptureMode.ActiveWindow); break;
            case HotkeyAction.ShowToolbar: ShowToolbar(); break;
        }
    }

    public void ShowToolbar()
    {
        if (_toolbar is null)
        {
            _toolbar = new ToolbarWindow();
            _toolbar.CaptureRequested += StartCapture;
        }
        _toolbar.ShowOnCursorMonitor();
    }

    public void StartCapture(CaptureMode mode) => StartCapture(mode, 0);

    public async void StartCapture(CaptureMode mode, int delaySeconds)
    {
        Log.Info($"Capture requested: {mode}, delay {delaySeconds}s");
        _toolbar?.Hide();
        _card?.Hide();
        try
        {
            if (delaySeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            await _capture!.RunAsync(mode);
        }
        catch (Exception ex)
        {
            Log.Error("Capture failed", ex);
            _tray?.ShowError("Capture failed", ex.Message);
        }
    }

    private async void OnCaptured(CaptureResult result)
    {
        var copied = false;
        try
        {
            if (Settings.AutoCopy)
            {
                await ImageExport.CopyToClipboardAsync(result.Image);
                copied = true;
            }
            Log.Info($"Captured {result.Image.Width}x{result.Image.Height}, copied: {copied}");
        }
        catch (Exception ex)
        {
            Log.Error("Clipboard failed", ex);
            _tray?.ShowError("Couldn't copy to the clipboard", ex.Message);
        }

        if (_card is null)
        {
            _card = new CaptureCardWindow();
            _card.PinRequested += Pin;
        }
        _card.Show(result, copied);
    }

    private async void Pin(CaptureResult result)
    {
        var pin = new PinWindow(result);
        _pins.Add(pin);
        pin.ClickThroughChanged += _ => UpdateClickThroughItem();
        pin.Closed += (_, _) =>
        {
            _pins.Remove(pin);
            UpdateClickThroughItem();
        };
        await pin.ShowAsync();
    }

    private void UpdateClickThroughItem() => _tray?.SetClickThroughPins(_pins.Any(p => p.IsClickThrough));

    /// <summary>Click-through pins can't be hovered, so the tray offers the way back.</summary>
    public void ReleaseClickThroughPins()
    {
        foreach (var pin in _pins.Where(p => p.IsClickThrough).ToArray())
            pin.SetClickThrough(false);
    }

    private void OnPrintScreenOwnershipChanged(bool windowsOwnsKey)
    {
        _tray?.SetPrintScreenWarning(windowsOwnsKey);
        if (!windowsOwnsKey)
            _notice?.Close();
    }

    public void ShowPrintScreenNotice()
    {
        if (_notice is null)
        {
            _notice = new PrintScreenNoticeWindow();
            _notice.Closed += (_, _) => _notice = null;
        }
        _notice.BringToFront();
    }

    /// <summary>Called on a background thread when a second instance redirects to us.</summary>
    internal void OnRedirectedActivation(AppActivationArguments args)
    {
        Dispatcher.TryEnqueue(ShowToolbar);
    }

    public void Quit()
    {
        _hook?.Dispose();
        _ownership?.Dispose();
        _notice?.Close();
        _toolbar?.Close();
        _card?.Close();
        foreach (var pin in _pins.ToArray()) pin.Close();
        _tray?.Dispose();
        _tray = null;
        _screenSource.Dispose();
        Exit();
    }
}
