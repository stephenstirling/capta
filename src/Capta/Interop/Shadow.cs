using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stirling.Shared;

namespace Capta.Interop;

/// <summary>
/// Soft rounded-rectangle shadow behind a panel, approximating the mockups' CSS
/// <c>box-shadow</c> with stacked translucent layers. (Composition drop shadows don't
/// render in the transparent-backdrop windows this is used in.)
/// </summary>
public static class Shadow
{
    private const int Layers = 12;

    /// <param name="host">An empty grid laid out exactly where the panel is, behind it.</param>
    /// <param name="cornerRadius">Panel corner radius, in DIPs.</param>
    /// <param name="offsetY">CSS y-offset, in DIPs.</param>
    /// <param name="blur">CSS blur radius, in DIPs.</param>
    /// <param name="opacity">CSS shadow alpha.</param>
    public static void Attach(Grid host, double cornerRadius, double offsetY, double blur, double opacity)
    {
        host.IsHitTestVisible = false;
        // A Canvas doesn't clip, so layers can extend past the panel's bounds.
        var canvas = new Canvas();
        host.Children.Clear();
        host.Children.Add(canvas);

        var spread = blur / 2;
        // Equal layers that compound to the CSS alpha where they all overlap; fewer layers
        // overlap further out, which gives the falloff.
        var layerAlpha = 1 - Math.Pow(1 - opacity, 1.0 / Layers);
        var layers = new List<(Border Border, double Grow, double Shift)>();
        for (var i = Layers; i >= 1; i--)
        {
            var t = i / (double)Layers;           // 1 = outermost layer
            var grow = spread * t;
            var shift = offsetY * (1 - t * 0.5);  // inner layers sit closer to the full offset
            var border = new Border
            {
                CornerRadius = new CornerRadius(cornerRadius + grow),
                Background = new SolidColorBrush(StirlingColors.Shadow) { Opacity = layerAlpha },
            };
            canvas.Children.Add(border);
            layers.Add((border, grow, shift));
        }

        void Layout()
        {
            foreach (var (border, grow, shift) in layers)
            {
                Canvas.SetLeft(border, -grow);
                Canvas.SetTop(border, -grow + shift);
                border.Width = Math.Max(0, host.ActualWidth + 2 * grow);
                border.Height = Math.Max(0, host.ActualHeight + 2 * grow);
            }
        }
        host.SizeChanged += (_, _) => Layout();
        Layout();
    }
}
