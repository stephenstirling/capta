using Capta.Controls;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// Startup &amp; shortcuts (design/mockups/Startup.html). Also where Capta explains the
/// Print Screen conflict with Snipping Tool. Reused: hidden rather than closed.
/// </summary>
public sealed partial class StartupSettingsWindow : Window
{
    private const double PanelHeightDip = 900;

    private readonly App _app;
    private bool _loading;
    private HotkeyAction? _recording;

    private static readonly (HotkeyAction Action, string Label, string? Hint)[] ShortcutRows =
    [
        (HotkeyAction.Region, "Region capture", null),
        (HotkeyAction.FullScreen, "Full screen, straight to clipboard", null),
        (HotkeyAction.ActiveWindow, "Active window", null),
        (HotkeyAction.ShowToolbar, "Show the floating toolbar", "Becomes Record video once video capture is available"),
        (HotkeyAction.GrabText, "Grab text from screen", null),
    ];

    public StartupSettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        ThemeService.Register(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleRow);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.SetIcon("Assets/Capta.ico");
        TransparentBackdrop.PrepareWindow(this);

        Panel.SizeChanged += (_, _) =>
        {
            ShadowHost.Width = Panel.ActualWidth;
            ShadowHost.Height = Panel.ActualHeight;
        };
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 14, offsetY: 20, blur: 50, opacity: 0.5);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
        Shortcuts.Changed += BuildShortcutRows;
        BuildShortcutRows();
    }

    private void Hide()
    {
        StopRecording();
        AppWindow.Hide();
    }

    // ---- Shortcuts ----

    private void BuildShortcutRows()
    {
        ShortcutList.Children.Clear();
        for (var i = 0; i < ShortcutRows.Length; i++)
        {
            var (action, label, hint) = ShortcutRows[i];
            var row = new Grid { Style = (Style)Root.Resources["ShortcutRow"] };
            if (i < ShortcutRows.Length - 1) row.BorderThickness = new Thickness(0, 0, 0, 1);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = label, Style = (Style)Root.Resources["ShortcutLabel"] });
            if (hint is not null)
                text.Children.Add(new TextBlock { Text = hint, Style = (Style)Root.Resources["ShortcutHint"] });
            row.Children.Add(text);

            var recording = _recording == action;
            var keys = new Border
            {
                Style = (Style)Root.Resources[recording ? "KeyComboRecording" : "KeyCombo"],
                Child = new TextBlock
                {
                    Text = recording ? "Press a shortcut…" : Shortcuts.For(action).ToString(),
                    Style = (Style)Root.Resources["KeyComboText"],
                },
            };
            Grid.SetColumn(keys, 1);
            row.Children.Add(keys);

            var edit = new Button
            {
                Style = (Style)Root.Resources["EditShortcut"],
                Content = new LineIcon { Size = 16, Data = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Microsoft.UI.Xaml.Media.Geometry), "M4 20l4-1 11-11-3-3L5 16z") },
            };
            ToolTipService.SetToolTip(edit, recording ? "Cancel (Esc)" : $"Change the shortcut for {label.ToLowerInvariant()}");
            edit.Click += (_, _) =>
            {
                if (_recording == action) StopRecording();
                else StartRecording(action);
            };
            Grid.SetColumn(edit, 2);
            row.Children.Add(edit);

            ShortcutList.Children.Add(row);
        }
    }

    private void StartRecording(HotkeyAction action)
    {
        _recording = action;
        ShortcutMessage.Text = "Press the new shortcut. It needs Print Screen, Ctrl or Alt. Esc cancels.";
        BuildShortcutRows();
        _app.RecordShortcut(chord =>
        {
            _recording = null;
            if (chord is not { } c)
            {
                ShortcutMessage.Text = "";
            }
            else if (!c.IsAllowed)
            {
                ShortcutMessage.Text = $"{c} would get in the way of typing. Use Print Screen, Ctrl or Alt.";
            }
            else
            {
                var swapped = Shortcuts.Set(action, c);
                ShortcutMessage.Text = swapped is { } other
                    ? $"{c} was in use, so the two shortcuts swapped."
                    : "";
            }
            BuildShortcutRows();
        });
    }

    private void StopRecording()
    {
        if (_recording is null) return;
        _recording = null;
        _app.CancelShortcutRecording();
        ShortcutMessage.Text = "";
        BuildShortcutRows();
    }

    private void OnResetShortcuts(object sender, RoutedEventArgs e)
    {
        StopRecording();
        Shortcuts.ResetAll();
        ShortcutMessage.Text = "Shortcuts reset to the defaults.";
    }

    public async void ShowCentered(bool windowsOwnsPrintScreen)
    {
        await LoadAsync(windowsOwnsPrintScreen);

        var area = WindowHelper.CursorWorkArea();
        // Move onto the target monitor first so the window adopts its DPI before sizing.
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();
        var pad = Root.Padding;

        // 900px tall per the mockup, shorter on small screens (the content scrolls).
        var maxPanel = area.Height / scale - pad.Top - pad.Bottom - 16;
        Panel.Height = Math.Min(PanelHeightDip, maxPanel);
        this.ResizeDip(Panel.Width + pad.Left + pad.Right, Panel.Height + pad.Top + pad.Bottom);
        this.CenterOnCursorMonitor();
        this.BringToFront();
    }

    private async Task LoadAsync(bool windowsOwnsPrintScreen)
    {
        _loading = true;
        try
        {
            StartWithWindows.IsOn = StartupTaskService.IsEnabled(await StartupTaskService.GetStateAsync());
            StartQuietly.IsOn = Settings.StartQuietly;
            ShowToolbarAtStartup.IsOn = Settings.ShowToolbarAtStartup;
            KeepRunning.IsOn = Settings.KeepRunningWhenToolbarClosed;
            SetPrintScreenOwnership(windowsOwnsPrintScreen);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Shows the warning card while Windows owns Print Screen, otherwise the success card.</summary>
    public void SetPrintScreenOwnership(bool windowsOwnsKey)
    {
        ConflictCard.Visibility = windowsOwnsKey ? Visibility.Visible : Visibility.Collapsed;
        OwnedCard.Visibility = windowsOwnsKey ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---- Startup ----

    private async void OnStartWithWindows(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var current = await StartupTaskService.GetStateAsync();
        if (StartupTaskService.IsLocked(current))
        {
            // Only the user can undo DisabledByUser (or policy); send them to the right page.
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
        }
        else
        {
            await StartupTaskService.SetEnabledAsync(StartWithWindows.IsOn);
        }

        _loading = true;
        StartWithWindows.IsOn = StartupTaskService.IsEnabled(await StartupTaskService.GetStateAsync());
        _loading = false;
        await _app.RefreshTrayAsync();
    }

    private void OnStartQuietly(object sender, RoutedEventArgs e)
    {
        if (!_loading) Settings.StartQuietly = StartQuietly.IsOn;
    }

    private void OnShowToolbarAtStartup(object sender, RoutedEventArgs e)
    {
        if (!_loading) Settings.ShowToolbarAtStartup = ShowToolbarAtStartup.IsOn;
    }

    private void OnKeepRunning(object sender, RoutedEventArgs e)
    {
        if (!_loading) Settings.KeepRunningWhenToolbarClosed = KeepRunning.IsOn;
    }

    // ---- Print Screen, links, close ----

    private async void OnOpenKeyboardSettings(object sender, RoutedEventArgs e) =>
        await PrintScreenOwnership.OpenSettingsAsync();

    private async void OnGitHub(object sender, RoutedEventArgs e) => await Links.OpenAsync(Links.GitHub);

    private async void OnBuyMeACoffee(object sender, RoutedEventArgs e) => await Links.OpenAsync(Links.BuyMeACoffee);

    private void OnDone(object sender, RoutedEventArgs e) => Hide();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Hide();
    }
}
