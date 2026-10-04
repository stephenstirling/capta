using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Capta;

/// <summary>
/// Custom entry point: Capta is a single-instance tray app, so a second launch
/// (Start menu, startup task firing twice, etc.) redirects to the running instance.
/// </summary>
public static class Program
{
    private const string InstanceKey = "Capta.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Capta.Services.Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        Capta.Services.Log.Info($"Starting (pid {Environment.ProcessId})");
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectToExistingInstance())
        {
            Capta.Services.Log.Info("Redirected activation to the running instance");
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool RedirectToExistingInstance()
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            main.Activated += (_, e) => App.Current?.OnRedirectedActivation(e);
            return false;
        }

        // Redirect off the STA thread so the COM call can't deadlock against it.
        Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
        return true;
    }
}
