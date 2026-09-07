using NightInjection.Core.Models;

namespace NightInjection.UI.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickInjectionFilesAsync();
    Task<string?> PickFolderAsync(string title);
    Task<string?> SaveTextAsync(string suggestedName, string extension, string content);
}

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string primaryText);
    Task ShowAsync(string title, string message);
}

public interface IClipboardService
{
    void SetText(string text);
}

public interface IThemeService
{
    void Apply(AppTheme theme);
}
