using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class Ft4StationSetupWindow : Window
{
    public Ft4StationSetupWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        var focusCallsign = DataContext is Ft4StationSetupViewModel vm && vm.FocusCallsign;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var box = focusCallsign ? CallsignBox : LocatorBox;
            box?.Focus();
            box?.SelectAll();
        });
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not Ft4StationSetupViewModel vm || !vm.TrySave())
            return;

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
