using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace Capta.Views;

/// <summary>
/// First-run setup (design/mockups/FirstRun.html): welcome, Print Screen, startup and theme,
/// Ocula and the capture folder, done. Shown once; closing it counts as done.
/// </summary>
public sealed partial class FirstRunWindow : Window
{
    private const int LastStep = 4;
    private static readonly string[] PrimaryLabels = ["Get started", "Next", "Next", "Next", "Finish"];

    private readonly App _app;
    private readonly StackPanel[] _steps;
    private int _step;
    private bool _windowsOwnsKey;
    private bool _loading;

    public FirstRunWindow(App app)
    {
        _app = app;
        InitializeComponent();
        ThemeService.Register(this);
        _steps = [Step0, Step1, Step2, Step3, Step4];
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleRow);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.SetIcon("Assets/Capta.ico");
        TransparentBackdrop.PrepareWindow(this);

        ShadowHost.Width = Panel.Width;
        ShadowHost.Height = Panel.Height;
        ShadowHost.HorizontalAlignment = Panel.HorizontalAlignment = HorizontalAlignment.Left;
        ShadowHost.VerticalAlignment = Panel.VerticalAlignment = VerticalAlignment.Top;
        Interop.Shadow.Attach(ShadowHost, cornerRadius: 14, offsetY: 20, blur: 50, opacity: 0.5);
        Interop.ClickThroughMargins.Attach(this, Panel);

        StartWithWindows.Toggled += async (_, _) =>
        {
            if (!_loading) await StartupTaskService.SetEnabledAsync(StartWithWindows.IsOn);
        };
        StartQuietly.Toggled += (_, _) =>
        {
            if (!_loading) Settings.StartQuietly = StartQuietly.IsOn;
        };
        SendToOcula.Toggled += (_, _) =>
        {
            if (!_loading) Settings.SendToOculaAutomatically = SendToOcula.IsOn;
        };

        // Closing early still counts as done; everything is also in Startup & shortcuts.
        Closed += (_, _) => Settings.FirstRunDone = true;
    }

    public async void ShowCentered(bool windowsOwnsPrintScreen)
    {
        _loading = true;
        try
        {
            StartWithWindows.IsOn = StartupTaskService.IsEnabled(await StartupTaskService.GetStateAsync());
            StartQuietly.IsOn = Settings.StartQuietly;
            SendToOcula.IsOn = Settings.SendToOculaAutomatically;
        }
        finally
        {
            _loading = false;
        }
        UpdateThemeRings();
        UpdateFolderText();
        await UpdateOculaAsync();
        SetPrintScreenOwnership(windowsOwnsPrintScreen);
        ShowStep(0);

        var pad = Root.Padding;
        this.ResizeDip(Panel.Width + pad.Left + pad.Right, Panel.Height + pad.Top + pad.Bottom);
        this.CenterOnCursorMonitor();
        this.BringToFront();
    }

    // ---- Steps ----

    private void ShowStep(int step)
    {
        _step = step;
        for (var i = 0; i < _steps.Length; i++)
            _steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;

        Dots.Children.Clear();
        for (var i = 0; i <= LastStep; i++)
        {
            var key = i == step ? "DotCurrent" : i < step ? "DotDone" : "DotTodo";
            Dots.Children.Add(new Border { Style = (Style)Root.Resources[key] });
        }

        BackButton.Visibility = step is > 0 and < LastStep ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Content = PrimaryLabels[step];
        UpdateFooter();
    }

    /// <summary>On the Print Screen step, the primary is neutral (with Skip) until Windows lets go of the key.</summary>
    private void UpdateFooter()
    {
        var waiting = _step == 1 && _windowsOwnsKey;
        SkipButton.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        WaitingButton.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetPrintScreenOwnership(bool windowsOwnsKey)
    {
        _windowsOwnsKey = windowsOwnsKey;
        PrintScreenPending.Visibility = windowsOwnsKey ? Visibility.Visible : Visibility.Collapsed;
        PrintScreenDone.Visibility = windowsOwnsKey ? Visibility.Collapsed : Visibility.Visible;
        UpdateFooter();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step < LastStep)
        {
            ShowStep(_step + 1);
            return;
        }

        var showToolbar = ShowToolbarNow.IsChecked == true;
        Close();
        if (showToolbar) _app.ShowToolbar();
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowStep(Math.Max(0, _step - 1));

    private async void OnOpenKeyboardSettings(object sender, RoutedEventArgs e) =>
        await PrintScreenOwnership.OpenSettingsAsync();

    // ---- Theme ----

    private void OnTheme(object sender, RoutedEventArgs e)
    {
        ThemeService.SetTheme(Enum.Parse<AppTheme>((string)((FrameworkElement)sender).Tag));
        UpdateThemeRings();
    }

    private void UpdateThemeRings()
    {
        var theme = Settings.Theme;
        ThemeSystemRing.Style = Ring(theme == AppTheme.System);
        ThemeLightRing.Style = Ring(theme == AppTheme.Light);
        ThemeDarkRing.Style = Ring(theme == AppTheme.Dark);
    }

    private Style Ring(bool on) => (Style)Root.Resources[on ? "RingOn" : "RingOff"];

    // ---- Ocula and the capture folder ----

    private async Task UpdateOculaAsync()
    {
        var installed = await CaptureActions.IsOculaInstalledAsync();
        OculaCheck.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        SendToOcula.IsEnabled = installed;
        if (installed)
        {
            OculaTitle.Text = "Ocula is installed";
            OculaDetail.Text = "Captures appear in its \"All captures\" smart folder, searchable by the text inside them.";
            FolderNote.Text = "Save starts in this folder.";
        }
        else
        {
            OculaTitle.Text = "Ocula is coming soon";
            OculaDetail.Text = "A free companion app for browsing and searching your captures, coming to the Microsoft Store.";
            FolderNote.Text = "Until then, Save starts in the folder above.";
        }
    }

    private void UpdateFolderText()
    {
        var folder = Settings.CaptureFolder;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        FolderText.Text = folder.StartsWith(pictures, StringComparison.OrdinalIgnoreCase)
            ? "Pictures" + folder[pictures.Length..].Replace("\\", " › ")
            : folder;
        ToolTipService.SetToolTip(FolderText, folder);
    }

    private async void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        var result = await picker.PickSingleFolderAsync();
        if (result is null) return;
        Settings.CaptureFolder = result.Path;
        UpdateFolderText();
    }
}
