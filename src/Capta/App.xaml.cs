using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Capta.Views;
using Capta.Overlay;
using Capta.Recording;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;
using Windows.Graphics;

namespace Capta;

public partial class App : Application
{
    // Unchanged from the old notice window so existing installs don't see the explanation twice.
    private const string NoticeShownKey = "PrintScreenNotice.Shown";

    private TrayIconService? _tray;
    private PrintScreenHook? _hook;
    private PrintScreenOwnership? _ownership;
    private StartupSettingsWindow? _settings;
    private TrayFlyoutWindow? _flyout;
    private FirstRunWindow? _firstRun;
    private ColourHdrWindow? _colour;
    private readonly CaptureHistory _history = new();

    public CaptureHistory History => _history;
    private ToolbarWindow? _toolbar;
    private CaptureCoordinator? _capture;
    private readonly GraphicsCaptureSource _screenSource = new();
    private CaptureCardWindow? _card;
    private readonly List<PinWindow> _pins = [];
    private ScreenRecorder? _recorder;
    private RecordingBarWindow? _recordBar;
    private (MonitorInfo Monitor, RectInt32 Area) _recorded;
    private bool _recordingBusy;

    public static new App? Current => (App?)Application.Current;

    public DispatcherQueue Dispatcher { get; } = DispatcherQueue.GetForCurrentThread();

    public App()
    {
        InitializeComponent();
        // No main window: the process lives in the tray until the user chooses Exit.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        // A tray app should survive a failing window: log it and keep the hook and tray alive.
        UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            e.Handled = true;
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Starts in the tray; which windows appear is governed by Startup & shortcuts.
        Settings.ApplyCaptureOptions();
        _capture = new CaptureCoordinator(_screenSource);
        _capture.Captured += OnCaptured;
        _capture.ColourPicked += PinColour;
        _capture.RecordRequested += StartRecording;
        _capture.SettingsRequested += page =>
        {
            if (page == SettingsPage.ColourAndHdr) ShowColourAndHdr();
            else ShowSettings();
        };

        _tray = new TrayIconService(this);
        _tray.Create();

        Shortcuts.Load();
        _hook = new PrintScreenHook(Dispatcher);
        _hook.Pressed += OnHotkey;
        _hook.Install();
        Log.Info("Tray icon and Print Screen hook installed");
        _ = _history.LoadAsync();

        _ownership = new PrintScreenOwnership();
        _ownership.Changed += owned => Dispatcher.TryEnqueue(() => OnPrintScreenOwnershipChanged(owned));
        _ownership.StartWatching();
        Log.Info($"Windows owns Print Screen: {_ownership.WindowsOwnsKey}");
        OnPrintScreenOwnershipChanged(_ownership.WindowsOwnsKey);

        // Explain the Print Screen conflict once; afterwards the tray menu carries the warning.
        var settings = ApplicationData.Current.LocalSettings.Values;
        var explainConflict = _ownership.WindowsOwnsKey && !settings.ContainsKey(NoticeShownKey);
        if (explainConflict)
            settings[NoticeShownKey] = true;
        if (!Settings.FirstRunDone)
            ShowFirstRun(); // covers the Print Screen conflict as its second step
        else if (explainConflict || !Settings.StartQuietly)
            ShowSettings();
        if (Settings.ShowToolbarAtStartup)
            ShowToolbar();

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
            case HotkeyAction.Record: ToggleRecording(); break;
            case HotkeyAction.GrabText: StartCapture(CaptureMode.GrabText); break;
        }
    }

    public void ShowToolbar()
    {
        if (_toolbar is null)
        {
            _toolbar = new ToolbarWindow();
            _toolbar.CaptureRequested += StartCapture;
            _toolbar.SettingsRequested += ShowSettings;
            _toolbar.ColourRequested += ShowColourAndHdr;
            _toolbar.CloseRequested += OnToolbarCloseRequested;
        }
        _toolbar.ShowOnCursorMonitor();
    }

    public void StartCapture(CaptureMode mode) => StartCapture(mode, 0);

    public async void StartCapture(CaptureMode mode, int delaySeconds)
    {
        if (mode == CaptureMode.Recording && _recorder is not null)
        {
            await StopRecordingAsync(); // Record again while recording stops it
            return;
        }
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
        if (result.Mode == CaptureMode.GrabText)
        {
            await GrabTextAsync(result);
            return;
        }

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

        _history.Add(result);
        ShowCard(result, copied);
        await SendToOculaIfEnabledAsync(result);
    }

    /// <summary>"Send every capture to Ocula automatically" (first-run setup), once Ocula is installed.</summary>
    private static async Task SendToOculaIfEnabledAsync(CaptureResult result)
    {
        try
        {
            if (Settings.SendToOculaAutomatically && await CaptureActions.IsOculaInstalledAsync())
                await CaptureActions.SendToOculaAsync(result);
        }
        catch (Exception ex)
        {
            Log.Error("Automatic send to Ocula failed", ex);
        }
    }

    /// <summary>Grab text: copy the selection's text (on-device OCR) and say how it went on the card.</summary>
    private async Task GrabTextAsync(CaptureResult result)
    {
        _history.Add(result);
        string status;
        var success = false;
        try
        {
            var text = await CaptureActions.CopyTextInImageAsync(result.Image);
            if (text is null)
            {
                status = "No text found";
            }
            else
            {
                var lines = text.Split(Environment.NewLine).Length;
                status = lines == 1 ? "Copied 1 line of text" : $"Copied {lines} lines of text";
                success = true;
            }
            Log.Info($"Grab text: {status}");
        }
        catch (Exception ex)
        {
            Log.Error("Grab text failed", ex);
            status = ex.Message;
        }
        ShowCard(result, copied: false, status, success);
    }

    /// <summary>Shows the after-capture card; from Recent in the tray flyout, nothing new was copied.</summary>
    // ---- Recording ----

    /// <summary>Ctrl+PrtSc: choose an area and start recording, or stop the current recording.</summary>
    public async void ToggleRecording()
    {
        if (_recorder is not null) await StopRecordingAsync();
        else StartCapture(CaptureMode.Recording);
    }

    private async void StartRecording(MonitorInfo monitor, RectInt32 area)
    {
        if (_recorder is not null || _recordingBusy) return;
        _recordingBusy = true;
        try
        {
            var path = CaptureActions.NewRecordingPath();
            var item = WinRTInterop.CreateItemForMonitor(monitor.Handle);
            var crop = new RectInt32(area.X - monitor.Bounds.X, area.Y - monitor.Bounds.Y, area.Width, area.Height);
            var microphone = Settings.RecordMicrophone && await MicrophoneAccess.RequestAsync();
            _recorder = await ScreenRecorder.StartAsync(item, crop, path, Settings.RecordSystemAudio, microphone);
            _recorded = (monitor, new RectInt32(area.X, area.Y, _recorder.Width, _recorder.Height));
            Log.Info($"Recording {_recorder.Width}x{_recorder.Height} to {path}");

            if (_recordBar is null)
            {
                _recordBar = new RecordingBarWindow();
                _recordBar.StopRequested += async () => await StopRecordingAsync();
                _recordBar.SystemAudioChanged = on =>
                {
                    Settings.RecordSystemAudio = on;
                    return Task.FromResult(_recorder?.SetSystemAudio(on) ?? false);
                };
                _recordBar.MicrophoneChanged = async on =>
                {
                    if (on && !await MicrophoneAccess.RequestAsync())
                    {
                        _tray?.ShowError("Microphone not available",
                            "Allow Capta to use the microphone in Settings > Privacy & security > Microphone.");
                        return false;
                    }
                    Settings.RecordMicrophone = on;
                    return _recorder?.SetMicrophone(on) ?? false;
                };
            }
            var recorder = _recorder;
            _recordBar.Show(_recorded.Area, monitor, () => recorder.Elapsed, recorder.SystemAudio, recorder.Microphone);
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed to start", ex);
            _tray?.ShowError("Couldn't start recording", ex.Message);
            if (_recorder is not null) await _recorder.DisposeAsync();
            _recorder = null;
        }
        finally
        {
            _recordingBusy = false;
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_recorder is not { } recorder || _recordingBusy) return;
        _recordingBusy = true;
        _recordBar?.Hide();
        try
        {
            var length = await recorder.StopAsync();
            await recorder.DisposeAsync();
            _recorder = null;

            // Card and Recent show a frame from the recording.
            var (monitor, area) = _recorded;
            double scale = Math.Min(1, 640.0 / Math.Max(area.Width, area.Height));
            var thumbnail = await VideoTools.ThumbnailAsync(recorder.Path, Math.Max(2, (int)(area.Width * scale)), Math.Max(2, (int)(area.Height * scale)));
            var under = WindowFinder.At(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
            var (app, title) = under is null ? (null, null) : WindowFinder.Describe(under.Handle);
            var result = new CaptureResult(thumbnail, monitor, area, CaptureMode.Recording, false)
            {
                VideoPath = recorder.Path,
                VideoLength = length,
                SavedPath = recorder.Path,
                SourceApp = app,
                WindowTitle = title,
            };
            _history.Add(result);
            ShowCard(result, status: $"Saved to {Path.GetFileName(Path.GetDirectoryName(recorder.Path))}");
            await SendToOculaIfEnabledAsync(result);
        }
        catch (Exception ex)
        {
            Log.Error("Recording failed", ex);
            _tray?.ShowError("Recording failed", ex.Message);
            _recorder = null;
        }
        finally
        {
            _recordingBusy = false;
        }
    }

    public void ShowCard(CaptureResult result, bool copied = false, string? status = null, bool success = true)
    {
        if (_card is null)
        {
            _card = new CaptureCardWindow();
            _card.PinRequested += Pin;
        }
        _card.Show(result, copied, status, success);
    }

    public void ShowTrayFlyout()
    {
        _flyout ??= new TrayFlyoutWindow(this);
        _flyout.ShowNearTray(_history.Items);
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

    /// <summary>C in the overlay: copy the colour's hex code and pin it as a chip.</summary>
    private void PinColour(Windows.UI.Color colour, Windows.Graphics.PointInt32 at)
    {
        var chip = new ColourChipWindow(colour);
        CaptureActions.CopyText(chip.Hex);
        Log.Info($"Picked colour {chip.Hex}");
        chip.ShowAt(at);
    }

    private void UpdateClickThroughItem() => _tray?.SetClickThroughPins(_pins.Any(p => p.IsClickThrough));

    /// <summary>Click-through pins can't be hovered, so the tray offers the way back.</summary>
    public void ReleaseClickThroughPins()
    {
        foreach (var pin in _pins.Where(p => p.IsClickThrough).ToArray())
            pin.SetClickThrough(false);
    }

    private void OnToolbarCloseRequested()
    {
        if (Settings.KeepRunningWhenToolbarClosed)
            _toolbar?.Hide();
        else
            Quit();
    }

    private void OnPrintScreenOwnershipChanged(bool windowsOwnsKey)
    {
        _tray?.SetPrintScreenWarning(windowsOwnsKey);
        _settings?.SetPrintScreenOwnership(windowsOwnsKey);
        _firstRun?.SetPrintScreenOwnership(windowsOwnsKey);
    }

    public void ShowColourAndHdr()
    {
        _colour ??= new ColourHdrWindow();
        _colour.ShowCentered();
    }

    public void ShowFirstRun()
    {
        if (_firstRun is null)
        {
            _firstRun = new FirstRunWindow(this);
            _firstRun.Closed += (_, _) => _firstRun = null;
        }
        _firstRun.ShowCentered(_ownership?.WindowsOwnsKey ?? false);
    }

    /// <summary>Startup &amp; shortcuts, which also explains the Print Screen conflict.</summary>
    public void ShowSettings()
    {
        _settings ??= new StartupSettingsWindow(this);
        _settings.ShowCentered(_ownership?.WindowsOwnsKey ?? false);
    }

    /// <summary>Captures the next key chord for changing a shortcut (null = cancelled with Esc).</summary>
    public void RecordShortcut(Action<Chord?> callback) => _hook?.Record(callback);

    public void CancelShortcutRecording() => _hook?.CancelRecording();

    public Task RefreshTrayAsync() => _tray?.RefreshStartupStateAsync() ?? Task.CompletedTask;

    /// <summary>Called on a background thread when a second instance redirects to us.</summary>
    internal void OnRedirectedActivation(AppActivationArguments args)
    {
        Dispatcher.TryEnqueue(ShowToolbar);
    }

    public void Quit()
    {
        if (_recorder is not null)
        {
            // Finish the file so it stays playable.
            try { _recorder.StopAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { Log.Error("Stopping the recording on exit failed", ex); }
        }
        _recordBar?.Close();
        _hook?.Dispose();
        _ownership?.Dispose();
        _settings?.Close();
        _flyout?.Close();
        _firstRun?.Close();
        _colour?.Close();
        _toolbar?.Close();
        _card?.Close();
        foreach (var pin in _pins.ToArray()) pin.Close();
        _tray?.Dispose();
        _tray = null;
        _screenSource.Dispose();
        Exit();
    }
}
