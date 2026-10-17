namespace OscarWatch.Core.Terminal;

/// <summary>One cell in an aprs.fi symbol sheet (16 columns).</summary>
public readonly record struct AprsSymbolCell(int Sheet, int Column, int Row);

/// <summary>
/// Maps an APRS symbol table character and symbol code onto the aprs.fi sprite sheets.
/// '/' is the primary table, '\' is the alternate table, and any other table character is an overlay
/// drawn on the alternate symbol.
/// </summary>
public static class AprsSymbolLayout
{
    public const int Columns = 16;
    public const int Sheets = 3;
    public const char First = '!';

    public static bool TryParse(string? symbol, out char table, out char code)
    {
        table = default;
        code = default;
        if (symbol is not { Length: 2 } || !IsSymbolChar(symbol[0]) || !IsSymbolChar(symbol[1]))
            return false;

        table = symbol[0];
        code = symbol[1];
        return true;
    }

    public static bool TryResolve(char table, char code, out AprsSymbolCell symbol, out AprsSymbolCell? overlay)
    {
        symbol = default;
        overlay = null;
        if (!IsSymbolChar(code))
            return false;

        if (table == '/')
        {
            symbol = Cell(0, code);
            return true;
        }

        if (!IsSymbolChar(table))
            return false;

        symbol = Cell(1, code);
        if (table != '\\')
            overlay = Cell(2, table);
        return true;
    }

    private static bool IsSymbolChar(char c) => c is >= First and <= '~';

    private static AprsSymbolCell Cell(int sheet, char c)
    {
        var index = c - First;
        return new AprsSymbolCell(sheet, index % Columns, index / Columns);
    }
}
