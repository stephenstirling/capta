using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Capta.Controls;

/// <summary>
/// A stroked line icon on a 24×24 grid, matching the mockups' inline SVGs
/// (round caps and joins). Its colour follows the inherited Foreground, so it
/// picks up button hover, pressed and disabled states.
/// </summary>
public sealed partial class LineIcon : UserControl
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(LineIcon), new PropertyMetadata(null, (d, e) => ((LineIcon)d)._path.Data = (Geometry)e.NewValue));

    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
        nameof(StrokeWidth), typeof(double), typeof(LineIcon), new PropertyMetadata(1.8, (d, e) => ((LineIcon)d)._path.StrokeThickness = (double)e.NewValue));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(LineIcon), new PropertyMetadata(18.0, (d, e) => ((LineIcon)d).ApplySize((double)e.NewValue)));

    public static readonly DependencyProperty DashedProperty = DependencyProperty.Register(
        nameof(Dashed), typeof(bool), typeof(LineIcon), new PropertyMetadata(false, (d, e) =>
            ((LineIcon)d)._path.StrokeDashArray = (bool)e.NewValue ? new DoubleCollection { 1.5, 1.5 } : null));

    private readonly Path _path = new()
    {
        Width = 24,
        Height = 24,
        StrokeThickness = 1.8,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private readonly Viewbox _box = new() { Width = 18, Height = 18 };

    public LineIcon()
    {
        IsTabStop = false;
        // Follow the inherited Foreground (a {Binding} doesn't see inherited changes such as a
        // ToggleButton switching to its checked colour).
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => _path.Stroke = Foreground);
        Loaded += (_, _) =>
        {
            _path.Stroke = Foreground;
            TrackOwnerEnabled();
        };
        _box.Child = new Canvas { Width = 24, Height = 24, Children = { _path } };
        Content = _box;
    }

    /// <summary>SVG path data on a 24×24 grid.</summary>
    public Geometry Data
    {
        get => (Geometry)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Stroke width in grid units (the SVG's stroke-width).</summary>
    public double StrokeWidth
    {
        get => (double)GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    /// <summary>Rendered size in DIPs (the SVG's width/height).</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>SVG <c>stroke-dasharray="3 3"</c> at stroke width 2.</summary>
    public bool Dashed
    {
        get => (bool)GetValue(DashedProperty);
        set => SetValue(DashedProperty, value);
    }

    /// <summary>
    /// Disabled buttons don't pass their disabled foreground (or IsEnabled) down to this
    /// control, so follow the nearest owning control directly and dim when it's disabled.
    /// </summary>
    private void TrackOwnerEnabled()
    {
        var parent = VisualTreeHelper.GetParent(this);
        while (parent is not null and not Control)
            parent = VisualTreeHelper.GetParent(parent);
        if (parent is not Control owner) return;

        void Apply() => Opacity = owner.IsEnabled ? 1 : 0.4;
        owner.IsEnabledChanged += (_, _) => Apply();
        Apply();
    }

    private void ApplySize(double size)
    {
        _box.Width = size;
        _box.Height = size;
    }
}
