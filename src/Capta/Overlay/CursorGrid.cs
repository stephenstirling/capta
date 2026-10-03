using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Capta.Overlay;

/// <summary>Grid that exposes UIElement.ProtectedCursor so the overlay can show a crosshair.</summary>
public partial class CursorGrid : Grid
{
    public InputCursor Cursor
    {
        get => ProtectedCursor;
        set => ProtectedCursor = value;
    }
}
