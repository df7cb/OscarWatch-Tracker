using Avalonia;
using Avalonia.Media;
using OscarWatch.Core.Geo;
using OscarWatch.Core.Models;

namespace OscarWatch.Controls;

internal static class PlotMarkerDrawing
{
    private static readonly Color OutlineDark = Color.Parse("#1a2028");

    public static void DrawSatelliteMarker(
        DrawingContext context,
        double x,
        double y,
        Color fill,
        bool isFocused,
        RenderResourceCache cache,
        bool belowMinimumElevation = false)
    {
        var radius = isFocused ? 7.0 : 5.0;
        var rect = new Rect(x - radius, y - radius, radius * 2, radius * 2);
        var fillColor = belowMinimumElevation
            ? Color.FromArgb(200, fill.R, fill.G, fill.B)
            : fill;

        context.DrawEllipse(
            cache.GetBrush(fillColor),
            cache.GetPen(OutlineDark, isFocused ? 3 : 2.5),
            rect);

        if (belowMinimumElevation)
        {
            var lightPen = cache.GetDashedPen(Colors.White, isFocused ? 2 : 1.5);
            context.DrawEllipse(null, lightPen, rect);
        }
        else
        {
            var lightPen = cache.GetPen(Colors.White, isFocused ? 2 : 1.5);
            context.DrawEllipse(null, lightPen, rect);
        }

        if (isFocused)
        {
            context.DrawEllipse(
                null,
                cache.GetPen(Colors.Gold, 2),
                new Rect(x - radius - 4, y - radius - 4, (radius + 4) * 2, (radius + 4) * 2));
        }
    }

    public static void DrawGroundStationMarker(DrawingContext context, double x, double y, UiPalette palette, RenderResourceCache cache)
    {
        const double radius = 6;
        var rect = new Rect(x - radius, y - radius, radius * 2, radius * 2);
        context.DrawEllipse(
            cache.GetBrush(palette.GroundStationFill),
            cache.GetPen(palette.GroundStationOutlineDark, 2.5),
            rect);
        context.DrawEllipse(null, cache.GetPen(Colors.White, 1.5), rect);
    }

    public static void DrawRemoteStationMarker(DrawingContext context, double x, double y, RenderResourceCache cache)
    {
        const double radius = 7;
        var fill = Color.Parse("#E69F00");
        var outline = Color.Parse("#1a2028");

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x, y - radius), true);
            ctx.LineTo(new Point(x + radius, y));
            ctx.LineTo(new Point(x, y + radius));
            ctx.LineTo(new Point(x - radius, y));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(cache.GetBrush(fill), cache.GetPen(outline, 2.5), geometry);
        context.DrawGeometry(null, cache.GetPen(Colors.White, 1.5), geometry);
    }

    /// <summary>Square marker for a heard Terminal station; faded stations get a translucent fill and dashed ring.</summary>
    public static void DrawHeardStationMarker(DrawingContext context, double x, double y, bool faded, RenderResourceCache cache)
    {
        const double half = 5;
        var fill = faded ? Color.FromArgb(150, 0x56, 0xB4, 0xE9) : Color.Parse("#56B4E9");
        var rect = new Rect(x - half, y - half, half * 2, half * 2);
        context.DrawRectangle(cache.GetBrush(fill), cache.GetPen(OutlineDark, 2.5), rect);
        context.DrawRectangle(null, faded ? cache.GetDashedPen(Colors.White, 1.5) : cache.GetPen(Colors.White, 1.5), rect);
    }

    public static void DrawFootprintMotionArrow(
        DrawingContext context,
        GeoCoordinate subpoint,
        double headingDeg,
        double footprintRadiusDeg,
        double centerX,
        double centerY,
        double mapWidth,
        double mapHeight,
        double mapCentreLongitudeDeg,
        Color color,
        bool isFocused,
        RenderResourceCache cache)
    {
        var (endLat, endLon) = SphericalGeo.DestinationPoint(
            subpoint.LatitudeDeg,
            subpoint.LongitudeDeg,
            2.0,
            headingDeg);
        endLon = EquirectangularProjection.NormalizeLongitudeNear(endLon, subpoint.LongitudeDeg);

        var (rawX, rawY) = EquirectangularProjection.GeoToPixel(
            subpoint.LatitudeDeg,
            subpoint.LongitudeDeg,
            mapWidth,
            mapHeight,
            mapCentreLongitudeDeg);
        var (endX, endY) = EquirectangularProjection.GeoToPixel(
            endLat,
            endLon,
            mapWidth,
            mapHeight,
            mapCentreLongitudeDeg);
        endX += centerX - rawX;

        var dx = endX - centerX;
        var dy = endY - centerY;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 2)
            return;

        dx /= len;
        dy /= len;

        var radiusPx = footprintRadiusDeg / 180.0 * mapHeight;
        var halfLen = Math.Clamp(radiusPx * 0.38, 7, isFocused ? 24 : 20);
        var wing = halfLen * 0.55;

        var tipX = centerX + dx * halfLen;
        var tipY = centerY + dy * halfLen;
        var baseX = centerX - dx * halfLen * 0.45;
        var baseY = centerY - dy * halfLen * 0.45;
        var leftX = tipX - dx * wing + dy * wing * 0.6;
        var leftY = tipY - dy * wing - dx * wing * 0.6;
        var rightX = tipX - dx * wing - dy * wing * 0.6;
        var rightY = tipY - dy * wing + dx * wing * 0.6;

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(tipX, tipY), true);
            g.LineTo(new Point(leftX, leftY));
            g.LineTo(new Point(baseX, baseY));
            g.LineTo(new Point(rightX, rightY));
            g.EndFigure(true);
        }

        var fillColor = Color.FromArgb((byte)(isFocused ? 220 : 190), color.R, color.G, color.B);
        context.DrawGeometry(
            cache.GetBrush(fillColor),
            cache.GetPen(Color.FromArgb(240, 26, 32, 40), isFocused ? 2 : 1.5),
            geometry);
    }

    public static void DrawMutualWindowStartMarker(DrawingContext context, double x, double y, RenderResourceCache cache)
    {
        const double size = 7;
        var fill = Color.Parse("#3DBB6D");
        var outline = Color.Parse("#1a2028");
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x, y - size), true);
            g.LineTo(new Point(x + size, y));
            g.LineTo(new Point(x, y + size));
            g.LineTo(new Point(x - size, y));
            g.EndFigure(true);
        }

        context.DrawGeometry(cache.GetBrush(fill), cache.GetPen(outline, 2), geometry);
        context.DrawGeometry(null, cache.GetPen(Colors.White, 1.5), geometry);
    }

    public static void DrawRotatorMarker(
        DrawingContext context,
        double x,
        double y,
        UiPalette palette,
        RenderResourceCache cache)
    {
        const double arm = 8;
        var color = palette.SkyPlotMinElRing;
        var pen = cache.GetPen(color, 2.5);
        context.DrawLine(pen, new Point(x - arm, y), new Point(x + arm, y));
        context.DrawLine(pen, new Point(x, y - arm), new Point(x, y + arm));

        const double radius = 3.5;
        context.DrawEllipse(
            cache.GetBrush(color),
            cache.GetPen(OutlineDark, 2),
            new Rect(x - radius, y - radius, radius * 2, radius * 2));
    }

    public static void DrawMutualWindowEndMarker(DrawingContext context, double x, double y, RenderResourceCache cache)
    {
        const double radius = 7;
        var fill = Color.Parse("#E69F00");
        var outline = Color.Parse("#1a2028");
        var rect = new Rect(x - radius, y - radius, radius * 2, radius * 2);
        context.DrawEllipse(cache.GetBrush(fill), cache.GetPen(outline, 2), rect);
        context.DrawEllipse(null, cache.GetPen(Colors.White, 1.5), rect);

        var pen = cache.GetPen(outline, 2.2);
        context.DrawLine(pen, new Point(x - 4, y - 4), new Point(x + 4, y + 4));
        context.DrawLine(pen, new Point(x + 4, y - 4), new Point(x - 4, y + 4));
    }
}
