using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OscarWatch.Controls;

/// <summary>Scales its child (text, padding and fixed column widths alike) so table rows stay aligned at any text size.</summary>
public sealed class ZoomDecorator : LayoutTransformControl
{
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ZoomDecorator, double>(nameof(Zoom), 1.0);

    static ZoomDecorator()
    {
        ZoomProperty.Changed.AddClassHandler<ZoomDecorator>((d, _) => d.ApplyZoom());
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    private void ApplyZoom()
    {
        var zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, 0.5, 3.0) : 1.0;
        LayoutTransform = Math.Abs(zoom - 1.0) < 0.001 ? null : new ScaleTransform(zoom, zoom);
    }
}
