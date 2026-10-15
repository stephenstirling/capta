using Capta.Capture;
using Capta.Interop;
using Capta.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Capta.Views;

/// <summary>
/// Colour &amp; HDR (design/mockups/Hdr.html): how HDR screens are converted to SDR.
/// Reused: hidden rather than closed.
/// </summary>
public sealed partial class ColourHdrWindow : Window
{
    private const double PanelHeightDip = 900;
    private bool _loading;

    public ColourHdrWindow()
    {
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
        Interop.ClickThroughMargins.Attach(this, Panel);

        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true;
            AppWindow.Hide();
        };
    }

    public void ShowCentered()
    {
        var cursor = WindowHelper.CursorPosition();
        BuildDisplayCards(cursor);
        Load();

        var area = WindowHelper.CursorWorkArea();
        AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        var scale = this.GetScale();
        var pad = Root.Padding;
        var maxPanel = area.Height / scale - pad.Top - pad.Bottom - 16;
        Panel.Height = Math.Min(PanelHeightDip, maxPanel);
        this.ResizeDip(Panel.Width + pad.Left + pad.Right, Panel.Height + pad.Top + pad.Bottom);
        this.CenterOnCursorMonitor();
        this.BringToFront();
    }

    // ---- Displays ----

    private void BuildDisplayCards(PointInt32 cursor)
    {
        DisplayCards.Children.Clear();
        DisplayCards.ColumnDefinitions.Clear();
        var monitors = Monitors.All();
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var colour = GraphicsCaptureSource.DisplayColour(monitor.Handle);
            var detail = new TextBlock
            {
                Text = colour switch
                {
                    { Hdr: true } c => $"HDR on · SDR white {c.SdrWhiteNits:0} nits",
                    { AutoColour: true } => "SDR · Auto colour management",
                    { } => "SDR · sRGB",
                    _ => "Colour info unavailable",
                },
                FontSize = 12,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Style = (Style)Root.Resources["CardDetail"],
            };
            if (colour is { Hdr: false, AutoColour: false })
                _ = ShowProfileNameAsync(detail, monitor.Handle);

            var card = new Border
            {
                Style = (Style)Root.Resources[monitor.Contains(cursor) ? "DisplayCardCurrent" : "DisplayCard"],
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = $"Display {i + 1}", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Bold },
                        detail,
                    },
                },
            };
            DisplayCards.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(card, i);
            DisplayCards.Children.Add(card);
        }
    }

    /// <summary>Names an SDR display's colour profile on its card once it has been read.</summary>
    private static async Task ShowProfileNameAsync(TextBlock detail, nint hmonitor)
    {
        var profile = await GraphicsCaptureSource.MonitorProfileAsync(hmonitor);
        if (profile is not null && IccProfile.Describe(profile) is { } name)
            detail.Text = $"SDR · {name}";
    }

    // ---- Settings ----

    private void Load()
    {
        _loading = true;
        try
        {
            MatchWindows.IsChecked = Settings.MatchWindowsSdrWhite;
            SdrWhiteSlider.Value = Settings.SdrWhiteNits;
            ApplySdrWhiteState();
            ApplyRollOff(Settings.RollOff);
            ApplyHdrHandling(Settings.HdrHandling);
            ApplyColourSpace(Settings.ColourSpace);
            CorrectProfiles.IsOn = Settings.CorrectMonitorProfiles;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>While matching Windows, the slider shows the current display's value; dragging it stops matching.</summary>
    private void ApplySdrWhiteState()
    {
        var match = MatchWindows.IsChecked == true;
        if (match)
        {
            var monitor = Monitors.AtPoint(WindowHelper.CursorPosition());
            if (GraphicsCaptureSource.DisplayColour(monitor.Handle) is { Hdr: true } c)
                SdrWhiteSlider.Value = Math.Clamp(c.SdrWhiteNits, SdrWhiteSlider.Minimum, SdrWhiteSlider.Maximum);
        }
        SdrWhiteText.Text = $"{SdrWhiteSlider.Value:0} nits";
    }

    private void OnSdrWhiteChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (SdrWhiteText is null || MatchWindows is null) return; // raised during InitializeComponent
        SdrWhiteText.Text = $"{e.NewValue:0} nits";
        if (_loading) return;
        if (MatchWindows.IsChecked == true)
        {
            MatchWindows.IsChecked = false;
            Settings.MatchWindowsSdrWhite = false;
        }
        Settings.SdrWhiteNits = e.NewValue;
        Settings.ApplyCaptureOptions();
    }

    private void OnMatchWindows(object sender, RoutedEventArgs e)
    {
        Settings.MatchWindowsSdrWhite = MatchWindows.IsChecked == true;
        if (!Settings.MatchWindowsSdrWhite)
            Settings.SdrWhiteNits = SdrWhiteSlider.Value; // start from what Windows was using
        _loading = true;
        ApplySdrWhiteState();
        _loading = false;
        Settings.ApplyCaptureOptions();
    }

    private void ApplyRollOff(HighlightRollOff rollOff)
    {
        foreach (var segment in new[] { RollSoft, RollBalanced, RollClip })
        {
            var selected = (string)segment.Tag == rollOff.ToString();
            segment.IsChecked = selected;
            segment.FontWeight = selected ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void OnRollOff(object sender, RoutedEventArgs e)
    {
        var rollOff = Enum.Parse<HighlightRollOff>((string)((ToggleButton)sender).Tag);
        Settings.RollOff = rollOff;
        ApplyRollOff(rollOff); // also keeps one segment selected
        Settings.ApplyCaptureOptions();
    }

    private void OnEmbedProfile(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.EmbedColourProfile = EmbedProfile.IsOn;
        Settings.ApplyCaptureOptions();
    }

    /// <summary>Display P3 files always carry their profile, so Embed shows on and can't be changed.</summary>
    private void ApplyColourSpace(ColourSpace space)
    {
        var p3 = space == ColourSpace.DisplayP3;
        ColourSpaceText.Text = p3 ? "Display P3" : "sRGB";
        SpaceSrgb.IsChecked = !p3;
        SpaceP3.IsChecked = p3;
        var loading = _loading;
        _loading = true;
        EmbedProfile.IsOn = p3 || Settings.EmbedColourProfile;
        _loading = loading;
        EmbedProfile.IsEnabled = !p3;
        EmbedProfileNote.Visibility = p3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnColourSpace(object sender, RoutedEventArgs e)
    {
        var space = Enum.Parse<ColourSpace>((string)((FrameworkElement)sender).Tag);
        Settings.ColourSpace = space;
        ApplyColourSpace(space); // also keeps one item checked
        Settings.ApplyCaptureOptions();
    }

    private void OnCorrectProfiles(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.CorrectMonitorProfiles = CorrectProfiles.IsOn;
        Settings.ApplyCaptureOptions();
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        Settings.MatchWindowsSdrWhite = true;
        Settings.SdrWhiteNits = 240;
        Settings.RollOff = HighlightRollOff.Clip;
        Settings.HdrHandling = HdrHandling.ToneMap;
        Settings.EmbedColourProfile = true;
        Settings.ColourSpace = ColourSpace.Srgb;
        Settings.CorrectMonitorProfiles = false;
        Settings.ApplyCaptureOptions();
        Load();
    }

    private void ApplyHdrHandling(HdrHandling handling)
    {
        var selected = (Style)Root.Resources["OptionSelected"];
        var normal = (Style)Root.Resources["Option"];
        foreach (var (button, on, dot, value) in new[]
        {
            (HdrToneMap, HdrToneMapOn, HdrToneMapDot, HdrHandling.ToneMap),
            (HdrKeep, HdrKeepOn, HdrKeepDot, HdrHandling.KeepHdr),
            (HdrBoth, HdrBothOn, HdrBothDot, HdrHandling.SaveBoth),
        })
        {
            var isSelected = value == handling;
            button.Style = isSelected ? selected : normal;
            on.Visibility = dot.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnHdrHandling(object sender, RoutedEventArgs e)
    {
        var handling = Enum.Parse<HdrHandling>((string)((Button)sender).Tag);
        Settings.HdrHandling = handling;
        ApplyHdrHandling(handling);
    }

    private void OnDone(object sender, RoutedEventArgs e) => AppWindow.Hide();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AppWindow.Hide();
    }
}
