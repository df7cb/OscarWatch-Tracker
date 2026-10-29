using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class Ft4SettingsWindow : Window
{
    public Ft4SettingsWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        if (DataContext is Ft4ViewModel vm)
        {
            vm.RefreshDevicesCommand.Execute(null);
            vm.RefreshEchoCalibration();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnBlockedCallsignKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;
        if (DataContext is not Ft4ViewModel vm || !vm.AddBlockedCallsignCommand.CanExecute(null))
            return;

        vm.AddBlockedCallsignCommand.Execute(null);
        e.Handled = true;
    }
}
