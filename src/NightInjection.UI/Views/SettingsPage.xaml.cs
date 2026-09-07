using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NightInjection.UI.ViewModels;

namespace NightInjection.UI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }
    private async void OnLoaded(object sender, RoutedEventArgs e) => await ViewModel.LoadCommand.ExecuteAsync(null);
}
