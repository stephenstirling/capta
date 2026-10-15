using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// "Version 1.1.x is available · What's new · Download", bottom-right of the monitor under the cursor.
/// Both buttons open the release page in the browser: Capta never downloads or installs an update
/// itself. Stays until dismissed. Reused: hidden rather than closed.
/// </summary>
public sealed partial class UpdateCardWindow : Window
{
    /// <summary>Gap between the panel and the work-area edges.</summary>
    private const double MarginDip = 24;

    private ReleaseInfo? _release;

    public UpdateCardWindow()
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

        Panel.SizeChanged += (_, _) =>
        {
            ShadowHost.Width = Panel.ActualWidth;
            ShadowHost.Height = Panel.ActualHeight;
        };
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 16, offsetY: 2, blur: 8, opacity: 0.22);
        Interop.ClickThroughMargins.Attach(this, Panel);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void Show(ReleaseInfo release, Version current)
    {
        _release = release;
        TitleText.Text = $"{release.Title} is available";
        DetailText.Text = $"You have {current.ToString(3)}";

        var area = WindowHelper.CursorWorkArea();
        // Move first so the window adopts the target monitor's DPI before sizing.
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();

        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var pad = Root.Padding;
        this.ResizeDip(Root.DesiredSize.Width, Root.DesiredSize.Height);
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            area.X + area.Width - size.Width - (int)((MarginDip - pad.Right) * scale),
            area.Y + area.Height - size.Height - (int)((MarginDip - pad.Bottom) * scale)));
        this.BringToFront();
    }

    public void Hide() => AppWindow.Hide();

    private async void OnWhatsNew(object sender, RoutedEventArgs e) => await OpenReleaseAsync();

    private async void OnDownload(object sender, RoutedEventArgs e) => await OpenReleaseAsync();

    private async Task OpenReleaseAsync()
    {
        if (_release is { } release)
            await Links.OpenAsync(new Uri(release.Url));
        Hide();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }
}
