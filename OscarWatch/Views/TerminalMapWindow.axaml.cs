using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OscarWatch.Core.Services;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class TerminalMapWindow : Window
{
    private const int MinWidthPx = 480;
    private const int MinHeightPx = 320;

    /// <summary>Raised with the callsign when a station marker is clicked.</summary>
    public event Action<string>? StationSelected;

    public TerminalMapWindow()
    {
        InitializeComponent();
        ApplySavedBounds();
        Map.StationMarkerClicked += (_, call) => StationSelected?.Invoke(call);
        Map.ViewChanged += (_, _) => UpdateZoomButtons();
        Opened += (_, _) =>
        {
            if (WindowStartupLocation == WindowStartupLocation.Manual)
                Position = QsoLogbookWindowBounds.ClampToVisibleArea(this, Position);
            (DataContext as TerminalMapViewModel)?.Start();
        };
        Closing += (_, _) => CaptureBounds();
        Closed += (_, _) =>
        {
            if (DataContext is TerminalMapViewModel vm)
            {
                vm.Stop();
                vm.Dispose();
            }
        };
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => Map.ZoomIn();

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => Map.ZoomOut();

    private void OnResetViewClick(object? sender, RoutedEventArgs e) => Map.ResetView();

    private void UpdateZoomButtons()
    {
        ZoomInButton.IsEnabled = Map.CanZoomIn;
        ZoomOutButton.IsEnabled = Map.CanZoomOut;
        ResetViewButton.IsEnabled = Map.IsViewChanged;
    }

    private static ISettingsService? Settings => App.Services.GetService(typeof(ISettingsService)) as ISettingsService;

    private void ApplySavedBounds()
    {
        if (Settings?.Current.Terminal is not { } t)
            return;
        if (t.MapWindowWidth is { } w && t.MapWindowHeight is { } h)
        {
            Width = Math.Max(w, MinWidthPx);
            Height = Math.Max(h, MinHeightPx);
        }

        if (t.MapWindowX is { } x && t.MapWindowY is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
    }

    private void CaptureBounds()
    {
        if (Settings is not { } settings || WindowState != WindowState.Normal)
            return;
        var t = settings.Current.Terminal;
        t.MapWindowWidth = Math.Max(MinWidthPx, (int)Math.Round(Width));
        t.MapWindowHeight = Math.Max(MinHeightPx, (int)Math.Round(Height));
        t.MapWindowX = Position.X;
        t.MapWindowY = Position.Y;
        settings.RequestSave();
    }
}
