using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OscarWatch;
using OscarWatch.Core.Ft4;
using OscarWatch.Localization;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class Ft4Window : Window
{
    private bool _closeConfirmed;
    private ScrollViewer? _decodeScroll;
    private Ft4DecodeRowViewModel? _contextRow;

    public Ft4Window()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not Ft4ViewModel vm)
            return;

        vm.Decodes.CollectionChanged += OnDecodeRowsChanged;
        await vm.OnWindowOpenedAsync().ConfigureAwait(true);
    }

    private void OnDecodeRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Newest lines are inserted at the top. The list keeps its scroll offset,
        // so each cycle appears above the viewport until the operator scrolls up.
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewStartingIndex != 0)
            return;

        Dispatcher.UIThread.Post(ScrollDecodesToTop, DispatcherPriority.Background);
    }

    private void ScrollDecodesToTop()
    {
        if (DecodeList.ItemCount == 0)
            return;

        DecodeList.ScrollIntoView(0);
        _decodeScroll ??= DecodeList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_decodeScroll is not null)
            _decodeScroll.Offset = new Vector(_decodeScroll.Offset.X, 0);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed)
            return;
        if (DataContext is not Ft4ViewModel vm || !vm.IsTransmissionActive)
            return;

        e.Cancel = true;
        var l = LocalizationService.Instance;
        var stop = await SimpleConfirmDialog.ShowAsync(
            this,
            l.Get("Ft4.CloseWhileTx.Title"),
            l.Get("Ft4.CloseWhileTx.Message")).ConfigureAwait(true);
        if (!stop)
            return;

        vm.StopTransmissionNow();
        _closeConfirmed = true;
        Close();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is not Ft4ViewModel vm)
            return;

        vm.Decodes.CollectionChanged -= OnDecodeRowsChanged;
        await vm.OnWindowClosedAsync().ConfigureAwait(true);
    }

    private async void OnEnterStationDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Ft4ViewModel vm)
            return;

        var dialog = new Ft4StationSetupWindow { DataContext = vm.CreateStationSetup() };
        var saved = await dialog.ShowDialog<bool>(this).ConfigureAwait(true);
        if (!saved)
            return;

        if (App.MainWindow?.DataContext is MainViewModel main)
            main.RefreshStationFromSettings();
        vm.OnStationDetailsSaved();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Ft4ViewModel vm)
            return;

        var dialog = new Ft4SettingsWindow { DataContext = vm };
        await dialog.ShowDialog(this).ConfigureAwait(true);
    }

    private void OnDecodeRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;

        _contextRow = (sender as Control)?.DataContext as Ft4DecodeRowViewModel;
        // Selecting a row answers that station, so a right-click must not select it.
        e.Handled = true;
    }

    private void OnDecodeContextMenuOpening(object? sender, CancelEventArgs e)
    {
        _contextRow ??= DecodeList.SelectedItem as Ft4DecodeRowViewModel;
        if (_contextRow is null
            || !_contextRow.Message.IsReceiveActivity
            || string.IsNullOrWhiteSpace(_contextRow.Message.CallDe))
        {
            _contextRow = null;
            e.Cancel = true;
        }
    }

    private void OnDecodeContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        // Click runs before Closed, so the row is still set when the item is chosen.
        Dispatcher.UIThread.Post(() => _contextRow = null);
    }

    private void OnPounceOnStationClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Ft4ViewModel vm && _contextRow is { } row)
            vm.PounceOnDecodeCommand.Execute(row);
        _contextRow = null;
    }

    private async void OnPounceCheckClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Ft4ViewModel vm)
            return;

        if (vm.IsPounceArmed)
        {
            vm.CancelPounceCommand.Execute(null);
        }
        else
        {
            var dialog = new Ft4PounceWindow(vm.LastPounceInput);
            var target = await dialog.ShowDialog<Ft4PounceTarget?>(this).ConfigureAwait(true);
            if (target is not null)
                vm.ArmPounce(target);
        }

        // The tick always shows whether pounce is armed, not what the click toggled it to.
        PounceCheckBox.SetCurrentValue(ToggleButton.IsCheckedProperty, vm.IsPounceArmed);
    }
}
