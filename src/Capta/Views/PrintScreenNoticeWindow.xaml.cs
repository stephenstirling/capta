using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Xaml;

namespace Capta.Views;

/// <summary>Explains that Windows still owns Print Screen and links to the setting that frees it.</summary>
public sealed partial class PrintScreenNoticeWindow : Window
{
    public PrintScreenNoticeWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        this.MakeToolWindow(alwaysOnTop: true);
        this.ResizeDip(460, 280);
        this.CenterOnCursorMonitor();
    }

    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        await PrintScreenOwnership.OpenSettingsAsync();
    }

    private void OnNotNow(object sender, RoutedEventArgs e) => Close();
}
