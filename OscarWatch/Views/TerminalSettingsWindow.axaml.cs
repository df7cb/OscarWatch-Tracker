using Avalonia.Controls;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class TerminalSettingsWindow : Window
{
    private TerminalSettingsViewModel? _vm;

    public TerminalSettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TerminalSettingsViewModel);
        Closed += (_, _) => Attach(null);
    }

    private void Attach(TerminalSettingsViewModel? vm)
    {
        if (_vm is not null)
            _vm.CloseRequested -= OnCloseRequested;
        _vm = vm;
        if (_vm is not null)
            _vm.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(bool saved) => Close(saved);
}
