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
    IDialogService dialogs) : ViewModelBase
{
    private InjectionPlan? _currentPlan;
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

    public async Task AddFilesAsync(IEnumerable<string> paths)
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

    [RelayCommand]
    private async Task BrowseAsync() => await AddFilesAsync(await picker.PickInjectionFilesAsync());

    [RelayCommand]
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

    [RelayCommand]
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
        if (IsBusy)
        {
            SetStatus("Another operation is already in progress.", true);
            return;
        }

        IsBusy = true;
        BuildFilePlanCommand.NotifyCanExecuteChanged();
        InjectFilesCommand.NotifyCanExecuteChanged();
        SetStatus($"Downloading the generated package for AppID {request.AppId}...");
        try
        {
            var result = await websiteImports.DownloadAsync(request);
            if (!result.Succeeded
                || result.FilePaths.Count == 0
                || string.IsNullOrWhiteSpace(result.TemporaryDirectory))
            {
                SetStatus(result.Message, true);
                return;
            }

            foreach (var path in result.FilePaths)
            {
                _temporaryImports[path] = result.TemporaryDirectory;
            }

            await AddFilesAsync(result.FilePaths);
            SetStatus(result.Message);
        }
        finally
        {
            IsBusy = false;
            BuildFilePlanCommand.NotifyCanExecuteChanged();
            InjectFilesCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanFetchAppId() => !string.IsNullOrWhiteSpace(AppId) && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanFetchAppId))]
    private async Task FetchAppIdAsync() => await RunPlanAsync(
        () => appIdService.BuildPlanAsync(settings.Current.SteamPath, AppId.Trim(), ForceOverwrite));

    [RelayCommand]
    private Task ExecuteCurrentPlanAsync() => ApplyCurrentPlanAsync();

    private async Task ApplyCurrentPlanAsync()
    {
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

        var confirmed = await dialogs.ConfirmAsync(
            "Apply injection plan?",
            $"{_currentPlan.Entries.Count} file(s) will be written. {_currentPlan.OverwriteCount} existing file(s) will be replaced.",
            "Apply changes");
        if (!confirmed)
        {
            return;
        }

        var executedPlan = _currentPlan;
        IsBusy = true;
        try
        {
            var result = await injection.ExecutePlanAsync(executedPlan);
            if (result.Succeeded && executedPlan.SourceKind == InjectionSourceKind.LocalFiles)
            {
                foreach (var file in Files)
                {
                    DeleteTemporaryImport(file.Path);
                }

                Files.Clear();
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

    [RelayCommand]
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

            _currentPlan = await factory();
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
