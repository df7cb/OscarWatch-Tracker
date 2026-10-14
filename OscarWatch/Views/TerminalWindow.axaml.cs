using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OscarWatch.Core.Services;
using OscarWatch.Core.Terminal;
using OscarWatch.Localization;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class TerminalWindow : Window
{
    private TerminalViewModel? _vm;
    private TerminalMapWindow? _mapWindow;

    public TerminalWindow()
    {
        InitializeComponent();
        ApplySavedBounds();
        Opened += OnOpened;
        Closing += (_, _) => CaptureBounds();
        Closed += OnClosed;
        AddZoomWheel(LogList, TerminalViewModel.LogPanel);
        AddZoomWheel(QsoList, TerminalViewModel.QsoPanel);
        AddZoomWheel(HeardList, TerminalViewModel.HeardPanel);
        CallersSplitter.DragCompleted += (_, _) => SaveCallersWidth();
        QsoHeardSplitter.DragCompleted += (_, _) => SaveQsoShare();
    }

    /// <summary>Ctrl+mouse wheel resizes a list's text. Tunnelling runs before the list's own scrolling.</summary>
    private void AddZoomWheel(Control list, string panel) =>
        list.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (_vm is null || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
                return;
            _vm.ChangeTextSize(panel, e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

    private static ISettingsService? Settings => App.Services.GetService(typeof(ISettingsService)) as ISettingsService;

    private void ApplySavedBounds()
    {
        if (Settings?.Current.Terminal is not { } t)
            return;

        if (t.WindowWidth is { } w && t.WindowHeight is { } h)
        {
            Width = w;
            Height = h;
        }

        if (t.WindowX is { } x && t.WindowY is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }

        if (t.WindowMaximised)
            WindowState = WindowState.Maximized;
    }

    /// <summary>Keeps the last normal size when closing maximised, so restoring later goes back to it.</summary>
    private void CaptureBounds()
    {
        if (Settings is not { } settings)
            return;

        var t = settings.Current.Terminal;
        t.WindowMaximised = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            t.WindowWidth = Math.Max(TerminalSettings.MinWindowWidth, (int)Math.Round(Width));
            t.WindowHeight = Math.Max(TerminalSettings.MinWindowHeight, (int)Math.Round(Height));
            t.WindowX = Position.X;
            t.WindowY = Position.Y;
        }

        settings.RequestSave();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (WindowStartupLocation == WindowStartupLocation.Manual)
            Position = QsoLogbookWindowBounds.ClampToVisibleArea(this, Position);

        if (DataContext is not TerminalViewModel vm)
            return;

        _vm = vm;
        vm.ScrollRequested += OnScrollRequested;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        ApplyCallersColumn();
        ApplyQsoShare();
        vm.OnWindowOpened();
        MessageBox.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _mapWindow?.Close();
        if (_vm is null)
            return;

        _vm.ScrollRequested -= OnScrollRequested;
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.OnWindowClosed();
        _vm = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.ShowCallers))
            ApplyCallersColumn();
    }

    private void ApplyCallersColumn()
    {
        if (_vm is null || BodyGrid.ColumnDefinitions.Count < 3)
            return;
        BodyGrid.ColumnDefinitions[2].Width = _vm.ShowCallers ? new GridLength(_vm.CallersWidth) : new GridLength(0);
    }

    private void ApplyQsoShare()
    {
        if (_vm is null || CallersGrid.RowDefinitions.Count < 3)
            return;
        CallersGrid.RowDefinitions[0].Height = new GridLength(_vm.QsoShare, GridUnitType.Star);
        CallersGrid.RowDefinitions[2].Height = new GridLength(1 - _vm.QsoShare, GridUnitType.Star);
    }

    private void SaveCallersWidth()
    {
        if (_vm is null || !_vm.ShowCallers || BodyGrid.ColumnDefinitions.Count < 3)
            return;
        _vm.SaveCallersLayout(BodyGrid.ColumnDefinitions[2].ActualWidth, null);
    }

    private void SaveQsoShare()
    {
        if (_vm is null || CallersGrid.RowDefinitions.Count < 3)
            return;
        var qso = CallersGrid.RowDefinitions[0].ActualHeight;
        var heard = CallersGrid.RowDefinitions[2].ActualHeight;
        if (qso + heard > 0)
            _vm.SaveCallersLayout(null, qso / (qso + heard));
    }

    /// <summary>Header grip Tag is "panel,column,+" or "panel,column,-" (a minus grip sits on the column's left edge).</summary>
    private static bool TryParseGrip(object? sender, out string panel, out int index, out int sign)
    {
        panel = "";
        index = -1;
        sign = 1;
        if (sender is not Control { Tag: string tag })
            return false;
        var parts = tag.Split(',');
        if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            return false;
        panel = parts[0];
        sign = parts[2] == "-" ? -1 : 1;
        return true;
    }

    private void OnColumnGripDragDelta(object? sender, VectorEventArgs e)
    {
        if (_vm is not null && TryParseGrip(sender, out var panel, out var index, out var sign))
            _vm.ResizeColumn(panel, index, sign * e.Vector.X);
    }

    private void OnColumnGripDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is not null && TryParseGrip(sender, out var panel, out var index, out _))
            _vm.ResetColumn(panel, index);
        e.Handled = true;
    }

    private void OnScrollRequested(TerminalMessageRowViewModel row) =>
        Dispatcher.UIThread.Post(() => LogList.ScrollIntoView(row), DispatcherPriority.Background);

    private void OnLogDoubleTapped(object? sender, TappedEventArgs e) =>
        _vm?.ReplyToSelectedCommand.Execute(null);

    private void OnQsoSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is not null && e.AddedItems.Count > 0 && e.AddedItems[0] is TerminalStationRowViewModel row)
            _vm.ToCall = row.Call;
    }

    private void OnMapClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;
        if (_mapWindow is not null)
        {
            _mapWindow.Activate();
            return;
        }

        if (App.Services.GetService(typeof(ISettingsService)) is not ISettingsService settings
            || App.Services.GetService(typeof(ILiveTrackingService)) is not ILiveTrackingService tracking
            || App.Services.GetService(typeof(ILocalizationService)) is not ILocalizationService localization)
            return;

        var window = new TerminalMapWindow
        {
            DataContext = new TerminalMapViewModel(settings, tracking, _vm.Service, localization)
        };
        window.StationSelected += call => _vm?.SelectStationByCall(call);
        window.Closed += (_, _) => _mapWindow = null;
        _mapWindow = window;
        window.Show(this);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var settingsVm = App.Services.GetService(typeof(TerminalSettingsViewModel)) as TerminalSettingsViewModel;
        if (settingsVm is null)
            return;

        var dialog = new TerminalSettingsWindow { DataContext = settingsVm };
        var saved = await dialog.ShowDialog<bool>(this).ConfigureAwait(true);
        if (saved)
            await _vm.ApplySettingsAsync().ConfigureAwait(true);
    }
}
