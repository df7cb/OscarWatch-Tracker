using Avalonia;
using Avalonia.Controls;

namespace OscarWatch.Controls;

/// <summary>
/// Bindable column widths for a <see cref="Grid"/> (for example <c>88,60,*</c>), so a table header and its
/// templated rows can share one resizable layout.
/// </summary>
public static class TableColumns
{
    public static readonly AttachedProperty<string?> LayoutProperty =
        AvaloniaProperty.RegisterAttached<Grid, string?>("Layout", typeof(TableColumns));

    static TableColumns()
    {
        LayoutProperty.Changed.AddClassHandler<Grid>((grid, e) => Apply(grid, e.NewValue as string));
    }

    public static string? GetLayout(Grid grid) => grid.GetValue(LayoutProperty);

    public static void SetLayout(Grid grid, string? value) => grid.SetValue(LayoutProperty, value);

    private static void Apply(Grid grid, string? layout)
    {
        if (string.IsNullOrWhiteSpace(layout))
            return;

        ColumnDefinitions parsed;
        try
        {
            parsed = ColumnDefinitions.Parse(layout);
        }
        catch (FormatException)
        {
            return;
        }

        if (grid.ColumnDefinitions.Count != parsed.Count)
        {
            grid.ColumnDefinitions = parsed;
            return;
        }

        for (var i = 0; i < parsed.Count; i++)
        {
            if (grid.ColumnDefinitions[i].Width != parsed[i].Width)
                grid.ColumnDefinitions[i].Width = parsed[i].Width;
        }
    }
}
