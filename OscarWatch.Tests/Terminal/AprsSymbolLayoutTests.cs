using OscarWatch.Core.Terminal;

namespace OscarWatch.Tests.Terminal;

public sealed class AprsSymbolLayoutTests
{
    [Theory]
    [InlineData("/[", 0, 10, 3)]
    [InlineData("/>", 0, 13, 1)]
    [InlineData("/-", 0, 12, 0)]
    [InlineData("/`", 0, 15, 3)]
    [InlineData("\\-", 1, 12, 0)]
    public void Primary_and_alternate_symbols_land_on_the_right_cell(string symbol, int sheet, int column, int row)
    {
        Assert.True(AprsSymbolLayout.TryParse(symbol, out var table, out var code));
        Assert.True(AprsSymbolLayout.TryResolve(table, code, out var cell, out var overlay));
        Assert.Equal(new AprsSymbolCell(sheet, column, row), cell);
        Assert.Null(overlay);
    }

    [Fact]
    public void Overlay_table_uses_the_alternate_symbol_and_an_overlay_glyph()
    {
        Assert.True(AprsSymbolLayout.TryParse("A>", out var table, out var code));
        Assert.True(AprsSymbolLayout.TryResolve(table, code, out var cell, out var overlay));
        Assert.Equal(new AprsSymbolCell(1, 13, 1), cell);
        Assert.Equal(new AprsSymbolCell(2, 0, 2), overlay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("x")]
    [InlineData(" [")]
    [InlineData("/ ")]
    public void Unusable_codes_are_rejected(string? symbol)
    {
        Assert.False(AprsSymbolLayout.TryParse(symbol, out _, out _));
    }

    [Fact]
    public void Vendored_sheets_are_sixteen_by_six_cells()
    {
        foreach (var name in new[] { "aprs-symbols-64-0.png", "aprs-symbols-64-1.png", "aprs-symbols-64-2.png" })
        {
            var path = FindSheet(name);
            var (width, height) = PngSize(path);
            Assert.Equal(64 * AprsSymbolLayout.Columns, width);
            Assert.Equal(64 * 6, height);
        }
    }

    private static string FindSheet(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "OscarWatch", "Assets", "AprsSymbols", name);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(name);
    }

    private static (int Width, int Height) PngSize(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return (width, height);
    }
}
