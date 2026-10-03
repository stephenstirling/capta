using Capta.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Capta;

public partial class App : Application
{
    private TrayIconService? _tray;

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

        await StartupTaskService.EnsureEnabledOnFirstRunAsync();
        await _tray.RefreshStartupStateAsync();
    }

    /// <summary>Called on a background thread when a second instance redirects to us.</summary>
    internal void OnRedirectedActivation(AppActivationArguments args)
    {
        Dispatcher.TryEnqueue(() => _tray?.ShowWelcomeNotification());
    }

    public void Quit()
    {
        _tray?.Dispose();
        _tray = null;
        Exit();
    }
}
