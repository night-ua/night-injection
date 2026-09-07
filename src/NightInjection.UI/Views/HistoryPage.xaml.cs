using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NightInjection.UI.ViewModels;

namespace NightInjection.UI.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }
    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.RefreshCommand.ExecuteAsync(null);
}
