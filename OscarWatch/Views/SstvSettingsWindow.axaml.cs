using Avalonia.Controls;
using Avalonia.Interactivity;
using OscarWatch.ViewModels;

namespace OscarWatch.Views;

public partial class SstvSettingsWindow : Window
{
    public SstvSettingsWindow()
    {
        InitializeComponent();
    }

    private async void OnBrowseOutputFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SstvViewModel vm)
            await vm.BrowseOutputFolderAsync(this).ConfigureAwait(true);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
