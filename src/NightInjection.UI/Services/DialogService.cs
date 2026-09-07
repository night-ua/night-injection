using Microsoft.UI.Xaml.Controls;

namespace NightInjection.UI.Services;

public sealed class DialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = App.Window.Content.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }
}
