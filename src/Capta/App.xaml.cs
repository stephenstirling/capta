using System.Diagnostics;
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
        Debug.WriteLine($"Capta hotkey: {action}");
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
        Dispatcher.TryEnqueue(() => _tray?.ShowWelcomeNotification());
    }

    public void Quit()
    {
        _hook?.Dispose();
        _ownership?.Dispose();
        _notice?.Close();
        _tray?.Dispose();
        _tray = null;
        Exit();
    }
}
