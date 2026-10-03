using System.Diagnostics;
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

    public static new App? Current => (App?)Application.Current;

    public DispatcherQueue Dispatcher { get; } = DispatcherQueue.GetForCurrentThread();

    public App()
    {
        InitializeComponent();
        // No main window: the process lives in the tray until the user chooses Exit.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
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

        _ownership = new PrintScreenOwnership();
        _ownership.Changed += owned => Dispatcher.TryEnqueue(() => OnPrintScreenOwnershipChanged(owned));
        _ownership.StartWatching();
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
        switch (action)
        {
            case HotkeyAction.ShowToolbar: ShowToolbar(); break;
            case HotkeyAction.Region: StartCapture(CaptureMode.Region); break;
            case HotkeyAction.Window: StartCapture(CaptureMode.Window); break;
            case HotkeyAction.FullScreen: StartCapture(CaptureMode.FullScreen); break;
        }
    }

    public void ShowToolbar()
    {
        if (_toolbar is null)
        {
            _toolbar = new ToolbarWindow();
            _toolbar.ModeChosen += StartCapture;
        }
        _toolbar.ShowOnCursorMonitor();
    }

    public async void StartCapture(CaptureMode mode)
    {
        _toolbar?.Hide();
        try
        {
            await _capture!.RunAsync(mode);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Capture failed: {ex}");
            _tray?.ShowError("Capture failed", ex.Message);
        }
    }

    private async void OnCaptured(CapturedImage image)
    {
        try
        {
            await ImageExport.CopyToClipboardAsync(image);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Clipboard failed: {ex}");
            _tray?.ShowError("Couldn't copy to the clipboard", ex.Message);
        }
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
        _tray?.Dispose();
        _tray = null;
        _screenSource.Dispose();
        Exit();
    }
}
