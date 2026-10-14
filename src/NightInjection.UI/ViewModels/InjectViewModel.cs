using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Validation;
using NightInjection.UI.Services;

namespace NightInjection.UI.ViewModels;

public sealed partial class SelectedInjectionFile : ObservableObject
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Size { get; init; }
    public required string Validation { get; init; }
}

public sealed partial class InjectViewModel(
    ISettingsService settings,
    IInjectionService injection,
    IAppIdService appIdService,
    IWebsiteImportService websiteImports,
    IFilePickerService picker,
    IDialogService dialogs,
    ISteamMetadataService? metadata = null) : ViewModelBase
{
    private InjectionPlan? _currentPlan;
    private CancellationTokenSource? _importCancellation;
    private string? _reviewedImportAppId;
    private readonly Dictionary<string, string> _temporaryImports = new(StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<SelectedInjectionFile> Files { get; } = [];
    public ObservableCollection<InjectionPlanEntry> PlannedEntries { get; } = [];

    [ObservableProperty]
    public partial bool IsDryRun { get; set; }

    [ObservableProperty]
    public partial bool ForceOverwrite { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FetchAppIdCommand))]
    public partial string AppId { get; set; } = string.Empty;

    public string PlanSummary { get; private set; } = "No plan built yet.";

    [ObservableProperty]
    public partial string ImportStage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double DownloadPercent { get; set; }

    [ObservableProperty]
    public partial string GameName { get; set; } = string.Empty;

    private bool CanEditFiles() => !IsBusy;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy))
        {
            BuildFilePlanCommand.NotifyCanExecuteChanged();
            InjectFilesCommand.NotifyCanExecuteChanged();
            FetchAppIdCommand.NotifyCanExecuteChanged();
            BrowseCommand.NotifyCanExecuteChanged();
            RemoveFileCommand.NotifyCanExecuteChanged();
            ClearFilesCommand.NotifyCanExecuteChanged();
            ExecuteCurrentPlanCommand.NotifyCanExecuteChanged();
            ClearPluginsCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task CancelImportAsync()
    {
        if (_importCancellation is not null) _importCancellation.Cancel();
        else if (!IsBusy)
        {
            await ClearFilesAsync();
            ImportStage = "Cancelled";
            SetStatus("Import cancelled. No Steam files were changed.");
        }
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        if (!IsBusy) await AddFilesCoreAsync(paths);
    }

    private async Task AddFilesCoreAsync(IEnumerable<string> paths)
    {
        var changed = false;
        var normalized = paths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var path in normalized)
        {
            if (Files.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var supported = extension is ".lua" or ".manifest" or ".zip";
            var info = new FileInfo(path);
            Files.Add(new SelectedInjectionFile
            {
                Path = path,
                Name = info.Name,
                Type = string.IsNullOrEmpty(extension) ? "Unknown" : extension.TrimStart('.').ToUpperInvariant(),
                Size = FormatBytes(info.Length),
                Validation = supported ? "Ready" : "Unsupported type"
            });
            changed = true;
        }

        if (changed)
        {
            await ResetPlanAsync();
        }

        BuildFilePlanCommand.NotifyCanExecuteChanged();
        InjectFilesCommand.NotifyCanExecuteChanged();
        SetStatus(Files.Count == 0 ? "Choose .lua, .manifest, or .zip files." : $"{Files.Count} file(s) selected.");
    }

    [RelayCommand(CanExecute = nameof(CanEditFiles))]
    private async Task BrowseAsync() => await AddFilesAsync(await picker.PickInjectionFilesAsync());

    [RelayCommand(CanExecute = nameof(CanEditFiles))]
    private async Task RemoveFileAsync(SelectedInjectionFile? item)
    {
        if (item is not null && Files.Remove(item))
        {
            DeleteTemporaryImport(item.Path);
            await ResetPlanAsync();
            BuildFilePlanCommand.NotifyCanExecuteChanged();
            InjectFilesCommand.NotifyCanExecuteChanged();
            SetStatus(Files.Count == 0 ? "Choose .lua, .manifest, or .zip files." : $"{Files.Count} file(s) selected.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditFiles))]
    private async Task ClearFilesAsync()
    {
        foreach (var file in Files)
        {
            DeleteTemporaryImport(file.Path);
        }

        Files.Clear();
        await ResetPlanAsync();
        BuildFilePlanCommand.NotifyCanExecuteChanged();
        InjectFilesCommand.NotifyCanExecuteChanged();
        SetStatus("Choose .lua, .manifest, or .zip files.");
    }

    private bool CanBuildFilePlan() => Files.Count > 0 && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBuildFilePlan))]
    private async Task BuildFilePlanAsync()
    {
        await RunPlanAsync(() => injection.BuildFilePlanAsync(
            Files.Where(static file => file.Validation == "Ready").Select(static file => file.Path).ToArray(),
            settings.Current.SteamPath));
    }

    [RelayCommand(CanExecute = nameof(CanBuildFilePlan))]
    private async Task InjectFilesAsync()
    {
        await BuildFilePlanAsync();
        if (_currentPlan?.IsValid == true)
        {
            await ApplyCurrentPlanAsync();
        }
    }

    public async Task ImportFromWebsiteAsync(WebsiteImportRequest request)
    {
        if (_reviewedImportAppId == request.AppId && Files.Count > 0)
        {
            SetStatus($"AppID {request.AppId} is already ready for review.");
            return;
        }
        if (IsBusy)
        {
            SetStatus("Another operation is already in progress.", true);
            return;
        }

        IsBusy = true;
        AppId = request.AppId;
        GameName = $"Steam AppID {request.AppId}";
        DownloadPercent = 0;
        _importCancellation = new CancellationTokenSource();
        var metadataTask = LoadGameNameAsync(request.AppId, _importCancellation.Token);
        var activeCancellation = _importCancellation;
        BuildFilePlanCommand.NotifyCanExecuteChanged();
        InjectFilesCommand.NotifyCanExecuteChanged();
        SetStatus($"Downloading the generated package for AppID {request.AppId}...");
        try
        {
            await ClearFilesAsync();
            var progress = new Progress<WebsiteImportProgress>(value =>
            {
                if (activeCancellation.IsCancellationRequested) return;
                ImportStage = value.Stage;
                DownloadPercent = value.TotalBytes is > 0
                    ? Math.Clamp(value.BytesReceived * 100d / value.TotalBytes.Value, 0, 100) : 0;
            });
            var result = await websiteImports.DownloadAsync(request, _importCancellation.Token, progress);
            if (!result.Succeeded
                || result.FilePaths.Count == 0
                || string.IsNullOrWhiteSpace(result.TemporaryDirectory))
            {
                ImportStage = result.ErrorCode is NightInjection.Core.Results.ImportErrorCode.CONFIGURATION_NOT_FOUND
                    or NightInjection.Core.Results.ImportErrorCode.GAME_NOT_SUPPORTED ? "Unsupported" : "Failed";
                SetStatus(result.Message, true);
                return;
            }

            foreach (var path in result.FilePaths)
            {
                _temporaryImports[path] = result.TemporaryDirectory;
            }

            await AddFilesCoreAsync(result.FilePaths);
            // Build a preview immediately; applying it is always a separate action.
            _currentPlan = await Task.Run(() => injection.BuildFilePlanAsync(result.FilePaths, settings.Current.SteamPath, _importCancellation.Token));
            foreach (var entry in _currentPlan.Entries) PlannedEntries.Add(entry);
            PlanSummary = _currentPlan.IsValid
                ? $"AppID {request.AppId}: {_currentPlan.Entries.Count} writes · {_currentPlan.OverwriteCount} overwrites · {FormatBytes(_currentPlan.TotalBytes)}"
                : string.Join(Environment.NewLine, _currentPlan.Errors);
            OnPropertyChanged(nameof(PlanSummary));
            _reviewedImportAppId = request.AppId;
            SetStatus(_currentPlan.IsValid ? result.Message + " Review the preview, then choose Apply Changes." : PlanSummary, !_currentPlan.IsValid);
        }
        catch (OperationCanceledException)
        {
            await ClearFilesAsync();
            SetStatus("Import cancelled. No Steam files were changed.");
            ImportStage = "Cancelled";
        }
        catch (Exception exception)
        {
            SetStatus($"Import failed: {exception.Message}", true);
        }
        finally
        {
            _importCancellation?.Cancel();
            await metadataTask;
            _importCancellation?.Dispose();
            _importCancellation = null;
            IsBusy = false;
            BuildFilePlanCommand.NotifyCanExecuteChanged();
            InjectFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadGameNameAsync(string id, CancellationToken token)
    {
        if (metadata is null || !settings.Current.FetchMetadata) return;
        try
        {
            var game = await metadata.GetAsync(id, token);
            if (AppId == id && !token.IsCancellationRequested) GameName = game.Name;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException)
        {
            // Game names are optional; a metadata outage must not fail an import.
        }
    }

    private bool CanFetchAppId() => !string.IsNullOrWhiteSpace(AppId) && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanFetchAppId))]
    private async Task FetchAppIdAsync() => await RunPlanAsync(
        () => appIdService.BuildPlanAsync(settings.Current.SteamPath, AppId.Trim(), ForceOverwrite));

    [RelayCommand(CanExecute = nameof(CanEditFiles))]
    private Task ExecuteCurrentPlanAsync() => ApplyCurrentPlanAsync();

    private async Task ApplyCurrentPlanAsync()
    {
        if (IsBusy) return;
        if (_currentPlan is null || !_currentPlan.IsValid)
        {
            SetStatus("Build a valid plan first.", true);
            return;
        }

        if (IsDryRun)
        {
            SetStatus("Dry Run complete. The plan is valid and no files were written.");
            return;
        }

        var executedPlan = _currentPlan;
        IsBusy = true;
        try
        {
            var confirmed = await dialogs.ConfirmAsync(
                "Apply injection plan?",
                $"{executedPlan.Entries.Count} file(s) will be written. {executedPlan.OverwriteCount} existing file(s) will be replaced.",
                "Apply changes");
            if (!confirmed) return;

            var result = await injection.ExecutePlanAsync(executedPlan);
            if (result.Succeeded && executedPlan.SourceKind == InjectionSourceKind.LocalFiles)
            {
                foreach (var file in Files)
                {
                    DeleteTemporaryImport(file.Path);
                }

                Files.Clear();
                _reviewedImportAppId = null;
                BuildFilePlanCommand.NotifyCanExecuteChanged();
                InjectFilesCommand.NotifyCanExecuteChanged();
            }

            SetStatus(result.Message, !result.Succeeded);
            if (result.Succeeded)
            {
                await dialogs.ShowAsync("Injection complete", result.Message);
            }
        }
        catch (Exception exception)
        {
            SetStatus($"Injection failed: {exception.Message}", true);
        }
        finally
        {
            _currentPlan = null;
            PlannedEntries.Clear();
            PlanSummary = "No plan built yet.";
            OnPropertyChanged(nameof(PlanSummary));
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditFiles))]
    private async Task ClearPluginsAsync()
    {
        if (!await dialogs.ConfirmAsync(
                "Clear the plug-in folder?",
                "This matches the legacy Clear behavior and removes every file directly inside Steam's config\\stplug-in folder.",
                "Clear files"))
        {
            return;
        }

        var result = await injection.ClearPluginsAsync(settings.Current.SteamPath);
        SetStatus(result.Message, !result.Succeeded);
    }

    private async Task RunPlanAsync(Func<Task<InjectionPlan>> factory)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SetStatus("Building a safe plan…");
        try
        {
            var previousPlan = _currentPlan;
            _currentPlan = null;
            PlannedEntries.Clear();
            PlanSummary = "No plan built yet.";
            OnPropertyChanged(nameof(PlanSummary));
            if (previousPlan is not null)
            {
                await injection.CleanupPlanAsync(previousPlan);
            }

            _currentPlan = await Task.Run(factory);
            foreach (var entry in _currentPlan.Entries)
            {
                PlannedEntries.Add(entry);
            }

            PlanSummary = _currentPlan.IsValid
                ? $"{_currentPlan.Entries.Count} writes · {_currentPlan.OverwriteCount} overwrites · {FormatBytes(_currentPlan.TotalBytes)}"
                : string.Join(Environment.NewLine, _currentPlan.Errors);
            OnPropertyChanged(nameof(PlanSummary));
            SetStatus(_currentPlan.IsValid ? "Plan ready for review." : PlanSummary, !_currentPlan.IsValid);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not build the plan: {exception.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResetPlanAsync()
    {
        _reviewedImportAppId = null;
        var previousPlan = _currentPlan;
        _currentPlan = null;
        PlannedEntries.Clear();
        PlanSummary = "No plan built yet.";
        OnPropertyChanged(nameof(PlanSummary));
        if (previousPlan is not null)
        {
            await injection.CleanupPlanAsync(previousPlan);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_048_576 => $"{bytes / 1_048_576d:F1} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };

    private void DeleteTemporaryImport(string path)
    {
        if (!_temporaryImports.Remove(path, out var temporaryDirectory))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        if (_temporaryImports.Values.Contains(temporaryDirectory, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
