using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OscarWatch.Localization;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class TerminalSettingsWindow : Window
{
    private TerminalSettingsViewModel? _vm;

    public TerminalSettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TerminalSettingsViewModel);
        Opened += (_, _) => _vm?.OnOpened();
        Closed += (_, _) =>
        {
            _vm?.OnClosed();
            Attach(null);
        };
    }

    private void Attach(TerminalSettingsViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.CloseRequested -= OnCloseRequested;
            _vm.BuiltIn.PickWavFile = null;
        }

        _vm = vm;
        if (_vm is not null)
        {
            _vm.CloseRequested += OnCloseRequested;
            _vm.BuiltIn.PickWavFile = PickWavFileAsync;
        }
    }

    private async Task<string?> PickWavFileAsync()
    {
        var l = LocalizationService.Instance;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = l.Get("Terminal.Modem.Decode.PickerTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(l.Get("Terminal.Modem.Decode.PickerType")) { Patterns = ["*.wav"] },
            ],
        }).ConfigureAwait(true);
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    private void OnCloseRequested(bool saved) => Close(saved);
}
