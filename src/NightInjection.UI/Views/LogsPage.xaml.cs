using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NightInjection.UI.ViewModels;

namespace NightInjection.UI.Views;

public sealed partial class LogsPage : Page
{
    public LogsViewModel ViewModel { get; }
    public LogsPage(LogsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ViewModel.RefreshCommand.Execute(null);

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.AutoScroll && LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
    }
}
