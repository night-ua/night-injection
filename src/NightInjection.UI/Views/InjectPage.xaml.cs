using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NightInjection.UI.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace NightInjection.UI.Views;

public sealed partial class InjectPage : Page
{
    public InjectViewModel ViewModel { get; }

    public InjectPage(InjectViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.DragUIOverride.Caption = "Add to injection plan";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        await ViewModel.AddFilesAsync(items.OfType<StorageFile>().Select(static file => file.Path));
    }
}
