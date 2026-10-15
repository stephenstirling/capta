using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using static Capta.Interop.NativeMethods;

namespace Capta.Views;

/// <summary>
/// After-capture card (design/mockups/Toolbar.html): bottom-right of the capture's monitor,
/// auto-hides after 6 s unless hovered or busy. Reused across captures (hidden, never closed).
/// </summary>
public sealed partial class CaptureCardWindow : Window
{
    /// <summary>Gap between the panel and the work-area edges.</summary>
    private const double MarginDip = 24;
    private static readonly TimeSpan AutoHide = TimeSpan.FromSeconds(6);

    private readonly DispatcherQueueTimer _timer;
    private CaptureResult? _current;
    private bool _busy;
    private bool _hovered;
    private bool _reviewOpen;
    private bool _oculaMissing;

    public event Action<CaptureResult>? PinRequested;

    private Flyout CopyMenu => (Flyout)CopySplit.Resources["CopyMenuFlyout"];

    public CaptureCardWindow()
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
            // The first measure can run before the content has laid out; re-anchor to the real size.
            if (_current is not null && AppWindow.IsVisible)
                PlaceOn(_current.Monitor);
        };
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 16, offsetY: 20, blur: 50, opacity: 0.55);
        Interop.ClickThroughMargins.Attach(this, Panel);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        CopyMenu.Opening += (_, _) =>
        {
            AutoCopySwitch.IsOn = Settings.AutoCopy;
            CopyPathRow.IsEnabled = _current?.SavedPath is not null;
            var hdr = _current?.Image.HdrPixels is not null;
            CopyHdrRow.IsEnabled = hdr;
            ToolTipService.SetToolTip(CopyHdrRow, hdr ? null : "Only for captures of an HDR screen");
            _busy = true;
            _timer!.Stop();
        };
        CopyMenu.Closed += (_, _) =>
        {
            _busy = false;
            if (!_hovered) RestartTimer();
        };

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = AutoHide;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Hide();
    }

    /// <param name="status">Overrides the status line (e.g. Grab text's result).</param>
    public async void Show(CaptureResult result, bool copied, string? status = null, bool success = true)
    {
        _current = result;
        var image = result.Image;
        var video = result.IsVideo;
        DetailText.Text = video
            ? $"Recording · {FormatLength(result.VideoLength)} · {result.ScreenBounds.Width} × {result.ScreenBounds.Height}"
            : $"{result.ModeName} · {image.Width} × {image.Height}" + (result.WasHdr ? " · tone-mapped from HDR" : "");
        ApplyKind(video);
        ShowRedaction(result);
        if (status is not null)
            SetStatus(status, success);
        else
            SetStatus(copied ? "Copied to clipboard" : "Capture ready", success: copied);

        ApplyOculaButton(await CaptureActions.IsOculaInstalledAsync());

        await SetThumbnailAsync(result);
        if (_current != result) return; // superseded by a newer capture

        PlaceOn(result.Monitor);
        this.BringToFront();
        RestartTimer();
    }

    /// <summary>Send to Ocula; or, without Ocula, Get Ocula (once it has a Store page) or a disabled button.</summary>
    private void ApplyOculaButton(bool installed)
    {
        _oculaMissing = !installed;
        var getOcula = !installed && CaptureActions.CanGetOcula;
        OculaText.Visibility = getOcula ? Visibility.Visible : Visibility.Collapsed;
        OculaButton.Padding = getOcula ? new Thickness(14, 0, 14, 0) : new Thickness(0);
        OculaButton.IsEnabled = installed || getOcula;
        ToolTipService.SetToolTip(OculaButton, installed ? "Send to Ocula"
            : getOcula ? "Get Ocula from the Microsoft Store"
            : "Send to Ocula (Ocula isn't installed)");
    }

    private async Task SetThumbnailAsync(CaptureResult result)
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(result.Image.ToSoftwareBitmap());
        if (_current == result) Thumbnail.Source = source; // not superseded by a newer capture
    }

    // ---- Personal info (blurred before the copy) ----

    private void ShowRedaction(CaptureResult result)
    {
        var redaction = result.Redaction;
        RedactionCard.Visibility = redaction is null ? Visibility.Collapsed : Visibility.Visible;
        if (redaction is not null) UpdateRedactionText(redaction);
    }

    private void UpdateRedactionText(Redaction redaction)
    {
        var summary = PersonalInfo.Describe(redaction.Matches);
        var blurred = redaction.BlurredCount;
        RedactionDetail.Text = blurred == redaction.Matches.Count ? $"{summary} · blurred automatically"
            : blurred == 0 ? $"{summary} · not blurred"
            : $"{blurred} of {redaction.Matches.Count} blurred";
        UndoRedactionButton.Content = blurred == 0 ? "Blur again" : "Undo";
    }

    /// <summary>Review: one switch per thing found.</summary>
    private void OnReviewOpening(object? sender, object e)
    {
        _reviewOpen = true;
        _timer.Stop();
        ReviewList.Children.Clear();
        if (_current?.Redaction is not { } redaction) return;
        var mono = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["CaptaMonoFont"];
        for (var i = 0; i < redaction.Matches.Count; i++)
        {
            var index = i;
            var match = redaction.Matches[i];
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = match.Label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = match.Text,
                FontFamily = mono,
                FontSize = 11.5,
                Opacity = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var toggle = new ToggleSwitch
            {
                IsOn = redaction.IsBlurred(index),
                OnContent = "",
                OffContent = "",
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };
            toggle.Toggled += async (_, _) => await ChangeRedactionAsync(() => redaction.Set(index, toggle.IsOn));
            var row = new Grid { MinHeight = 48, Padding = new Thickness(8, 4, 8, 4), ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(text);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            ReviewList.Children.Add(row);
        }
    }

    private void OnReviewClosed(object? sender, object e)
    {
        _reviewOpen = false;
        if (!_hovered && !_busy) RestartTimer();
    }

    private async void OnUndoRedaction(object sender, RoutedEventArgs e)
    {
        if (_current?.Redaction is not { } redaction) return;
        _timer.Stop();
        await ChangeRedactionAsync(() => redaction.SetAll(redaction.BlurredCount == 0));
        if (!_hovered && !_busy && !_reviewOpen) RestartTimer();
    }

    /// <summary>Applies a change to what's blurred: new image on the card, and on the clipboard if it was auto-copied.</summary>
    private async Task ChangeRedactionAsync(Action change)
    {
        if (_current is not { Redaction: { } redaction } result) return;
        try
        {
            change();
            result.Image = await Task.Run(redaction.Render);
            UpdateRedactionText(redaction);
            await SetThumbnailAsync(result);
            if (Settings.AutoCopy)
            {
                await ImageExport.CopyToClipboardAsync(result.Image);
                SetStatus("Copied to clipboard");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Changing the blurred areas failed", ex);
            SetStatus(ex.Message, success: false);
        }
    }

    /// <summary>Recordings: Play instead of Edit, file copies instead of image copies, GIF export, no pin.</summary>
    private void ApplyKind(bool video)
    {
        var image = video ? Visibility.Collapsed : Visibility.Visible;
        CopyImageRow.Visibility = CopyHdrRow.Visibility = CopyTextRow.Visibility = image;
        GifRow.Visibility = video ? Visibility.Visible : Visibility.Collapsed;
        CopyAsFileText.Text = video ? "Copy video file" : "Copy as file";
        EditText.Text = video ? "Play" : "Edit";
        EditIcon.Data = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
            typeof(Microsoft.UI.Xaml.Media.Geometry), video ? "M8 5v14l11-7z" : "M4 20l4-1 11-11-3-3L5 16z");
        ToolTipService.SetToolTip(EditButton, video ? "Play" : "Edit");
        PinButton.IsEnabled = !video;
        ToolTipService.SetToolTip(PinButton, video ? "Recordings can't be pinned" : "Pin to screen");
    }

    private static string FormatLength(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>Bottom-right of the monitor's work area; the window's padding holds the shadow.</summary>
    private void PlaceOn(MonitorInfo monitor)
    {
        var area = monitor.WorkArea;
        // Move first so the window adopts the target monitor's DPI before sizing.
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();

        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var pad = Root.Padding;
        var width = Math.Max(Root.DesiredSize.Width, Panel.ActualWidth + pad.Left + pad.Right);
        var height = Math.Max(Root.DesiredSize.Height, Panel.ActualHeight + pad.Top + pad.Bottom);
        // Size the client area: even a borderless window keeps a thin frame in its outer size.
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)));
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(
            area.X + area.Width - size.Width - (int)((MarginDip - pad.Right) * scale),
            area.Y + area.Height - size.Height - (int)((MarginDip - pad.Bottom) * scale)));
    }

    public void Hide()
    {
        _timer.Stop();
        CopyMenu.Hide();
        AppWindow.Hide();
    }

    private void RestartTimer()
    {
        _timer.Stop();
        _timer.Start();
    }

    private void SetStatus(string text, bool success = true)
    {
        StatusText.Text = text;
        SuccessDot.Visibility = success ? Visibility.Visible : Visibility.Collapsed;
        NeutralDot.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Hover keeps the card open ----

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovered = true;
        _timer.Stop();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovered = false;
        if (!_busy) RestartTimer();
    }

    // ---- Copy ----

    private async void OnCopy(object sender, RoutedEventArgs e) => await CopyImageAsync();

    private void OnCopyMenuClick(object sender, RoutedEventArgs e) => CopyMenu.ShowAt(CopySplit);

    private async void OnCopyImage(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        await CopyImageAsync();
    }

    private async Task CopyImageAsync()
    {
        if (_current is null) return;
        if (_current.VideoPath is { } video)
        {
            await RunAsync(async () =>
            {
                await CaptureActions.CopyFileAsync(video);
                SetStatus("Copied video file");
            });
            return;
        }
        await RunAsync(async () =>
        {
            await ImageExport.CopyToClipboardAsync(_current.Image);
            SetStatus("Copied to clipboard");
        });
    }

    private async void OnCopyText(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        await CopyTextAsync();
    }

    private async Task CopyTextAsync()
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            var text = await CaptureActions.CopyTextInImageAsync(_current.Image);
            if (text is null)
            {
                SetStatus("No text found", success: false);
                return;
            }
            var lines = text.Split(Environment.NewLine).Length;
            SetStatus(lines == 1 ? "Copied 1 line of text" : $"Copied {lines} lines of text");
        });
    }

    private async void OnCopyAsFile(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        if (_current is null) return;
        if (_current.IsVideo)
        {
            await CopyImageAsync(); // copies the MP4
            return;
        }
        await RunAsync(async () =>
        {
            await CaptureActions.CopyAsFileAsync(_current);
            SetStatus("Copied as file");
        });
    }

    private async void OnSaveGif(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        if (_current?.VideoPath is not { } video) return;
        await RunAsync(async () =>
        {
            var gif = Path.ChangeExtension(video, ".gif");
            SetStatus("Making GIF…");
            await Recording.VideoTools.SaveGifAsync(video, gif,
                progress: new Progress<double>(f => SetStatus($"Making GIF… {f:P0}")));
            await CaptureActions.CopyFileAsync(gif);
            SetStatus($"Saved {Path.GetFileName(gif)} · copied as file");
        });
    }

    private async void OnCopyHdr(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        if (_current is null) return;
        await RunAsync(async () =>
        {
            await CaptureActions.CopyAsHdrAsync(_current);
            SetStatus("Copied as HDR (JPEG XR)");
        });
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        CopyMenu.Hide();
        if (_current?.SavedPath is not { } path) return;
        CaptureActions.CopyText(path);
        SetStatus("Copied file path");
        RestartTimer();
    }

    private void OnAutoCopyToggled(object sender, RoutedEventArgs e) => Settings.AutoCopy = AutoCopySwitch.IsOn;

    private async void OnCopyAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CopyImageAsync();
    }

    private async void OnCopyTextAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await CopyTextAsync();
    }

    // ---- Edit, Save, Pin, Ocula ----

    private async void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            if (_current.VideoPath is { } video)
                await Windows.System.Launcher.LaunchFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(video));
            else
                await CaptureActions.EditAsync(_current);
            Hide();
        });
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        await RunAsync(async () =>
        {
            var path = _current.IsVideo
                ? await CaptureActions.SaveVideoAsAsync(_current, AppWindow.Id)
                : await CaptureActions.SaveAsAsync(_current, AppWindow.Id);
            if (path is null) return;
            _current.SavedPath = path;
            ((App)Application.Current).History.Update();
            SetStatus($"Saved {Path.GetFileName(path)}");
        });
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        PinRequested?.Invoke(_current);
        Hide();
    }

    private async void OnSendToOcula(object sender, RoutedEventArgs e)
    {
        if (_oculaMissing)
        {
            await RunAsync(CaptureActions.GetOculaAsync);
            return;
        }
        if (_current is null) return;
        await RunAsync(async () =>
        {
            await CaptureActions.SendToOculaAsync(_current);
            SetStatus("Sent to Ocula");
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Hide();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }

    /// <summary>Holds the card open while an action (e.g. a dialog) is in progress.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        _timer.Stop();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error("Card action failed", ex);
            SetStatus(ex.Message, success: false);
        }
        finally
        {
            _busy = false;
            if (!_hovered) RestartTimer();
        }
    }
}
