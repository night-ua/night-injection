using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NightInjection.UI.ViewModels;

namespace NightInjection.UI.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; }
    public LibraryPage(LibraryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshCommand.ExecuteAsync(null);
}
