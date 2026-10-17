using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using OscarWatch.Core.Terminal;
using Serilog;

namespace OscarWatch.Terminal;

/// <summary>Draws symbols from the vendored aprs.fi sprite sheets.</summary>
public static class AprsSymbolAtlas
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(AprsSymbolAtlas));
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bitmap?> Icons = new(StringComparer.Ordinal);

    private static Bitmap[]? _sheets;
    private static bool _failed;

    public static bool Draw(DrawingContext context, char table, char code, double x, double y, double size, bool faded)
    {
        if (!TryPlacement(table, code, out var sheet, out var source, out var overlaySheet, out var overlaySource))
            return false;

        var dest = new Rect(x - size / 2, y - size / 2, size, size);
        if (faded)
        {
            using (context.PushOpacity(0.55))
                Paint(context, sheet, source, overlaySheet, overlaySource, dest);
        }
        else
        {
            Paint(context, sheet, source, overlaySheet, overlaySource, dest);
        }

        return true;
    }

    private static void Paint(
        DrawingContext context,
        Bitmap sheet,
        Rect source,
        Bitmap? overlaySheet,
        Rect? overlaySource,
        Rect dest)
    {
        context.DrawImage(sheet, source, dest);
        if (overlaySheet is not null && overlaySource is { } overlay)
            context.DrawImage(overlaySheet, overlay, dest);
    }

    public static Bitmap? TryCreateIcon(string? symbol, int pixels = 32)
    {
        if (string.IsNullOrEmpty(symbol))
            return null;

        lock (Gate)
        {
            if (Icons.TryGetValue(symbol, out var cached))
                return cached;

            var created = RenderIcon(symbol, pixels);
            Icons[symbol] = created;
            return created;
        }
    }

    private static Bitmap? RenderIcon(string symbol, int pixels)
    {
        if (!AprsSymbolLayout.TryParse(symbol, out var table, out var code)
            || !TryPlacement(table, code, out var sheet, out var source, out var overlaySheet, out var overlaySource))
            return null;

        var icon = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
        using (var ctx = icon.CreateDrawingContext())
        {
            var dest = new Rect(0, 0, pixels, pixels);
            ctx.DrawImage(sheet, source, dest);
            if (overlaySheet is not null && overlaySource is { } overlay)
                ctx.DrawImage(overlaySheet, overlay, dest);
        }

        return icon;
    }

    private static bool TryPlacement(
        char table,
        char code,
        out Bitmap sheet,
        out Rect source,
        out Bitmap? overlaySheet,
        out Rect? overlaySource)
    {
        sheet = null!;
        source = default;
        overlaySheet = null;
        overlaySource = null;
        var sheets = Sheets;
        if (sheets is null || !AprsSymbolLayout.TryResolve(table, code, out var symbol, out var overlay))
            return false;
        if (!TryCell(sheets[symbol.Sheet], symbol, out source))
            return false;

        sheet = sheets[symbol.Sheet];
        if (overlay is { } over && TryCell(sheets[over.Sheet], over, out var overlayRect))
        {
            overlaySheet = sheets[over.Sheet];
            overlaySource = overlayRect;
        }

        return true;
    }

    private static bool TryCell(Bitmap bitmap, AprsSymbolCell cell, out Rect rect)
    {
        var cellSize = bitmap.Size.Width / AprsSymbolLayout.Columns;
        var x = cell.Column * cellSize;
        var y = cell.Row * cellSize;
        if (cellSize <= 0
            || cell.Column < 0
            || cell.Row < 0
            || x + cellSize > bitmap.Size.Width + 0.01
            || y + cellSize > bitmap.Size.Height + 0.01)
        {
            rect = default;
            return false;
        }

        rect = new Rect(x, y, cellSize, cellSize);
        return true;
    }

    private static Bitmap[]? Sheets
    {
        get
        {
            if (_sheets is not null || _failed)
                return _sheets;

            lock (Gate)
            {
                if (_sheets is not null || _failed)
                    return _sheets;

                try
                {
                    _sheets =
                    [
                        Load("aprs-symbols-64-0.png"),
                        Load("aprs-symbols-64-1.png"),
                        Load("aprs-symbols-64-2.png"),
                    ];
                }
                catch (Exception ex)
                {
                    _failed = true;
                    Log.Warning(ex, "APRS symbol sheets could not be loaded");
                }

                return _sheets;
            }
        }
    }

    private static Bitmap Load(string file)
    {
        var uri = new Uri($"avares://OscarWatch/Assets/AprsSymbols/{file}");
        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }
}
