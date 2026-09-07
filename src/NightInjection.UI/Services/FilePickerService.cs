using Windows.Storage.Pickers;

namespace NightInjection.UI.Services;

public sealed class FilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickInjectionFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".lua");
        picker.FileTypeFilter.Add(".manifest");
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(static file => file.Path).Where(static path => !string.IsNullOrWhiteSpace(path)).ToArray();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            CommitButtonText = title
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> SaveTextAsync(string suggestedName, string extension, string content)
    {
        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName)
        };
        picker.FileTypeChoices.Add($"{normalizedExtension.TrimStart('.').ToUpperInvariant()} file", [normalizedExtension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null;
        }

        await Windows.Storage.FileIO.WriteTextAsync(file, content);
        return file.Path;
    }
}
