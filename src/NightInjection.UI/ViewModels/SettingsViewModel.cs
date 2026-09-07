using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.UI.Services;

namespace NightInjection.UI.ViewModels;

public sealed partial class SettingsViewModel(
    ISettingsService settings,
    ISteamService steam,
    ICoverService covers,
    ILoaderPlanService loader,
    IFilePickerService picker,
    IThemeService themes,
    IDialogService dialogs,
    IAppPathService paths,
    IAppLogStore logStore) : ViewModelBase
{
    [ObservableProperty] public partial string SteamPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string CacheDirectory { get; set; } = string.Empty;
    [ObservableProperty] public partial AppTheme Theme { get; set; }
    [ObservableProperty] public partial bool AnimationsEnabled { get; set; }
    [ObservableProperty] public partial bool AutoScrollLogs { get; set; }
    [ObservableProperty] public partial bool RememberWindowSize { get; set; }
    [ObservableProperty] public partial bool DebugLogging { get; set; }
    [ObservableProperty] public partial bool FetchMetadata { get; set; }
    [ObservableProperty] public partial string CacheSummary { get; set; } = "Calculating…";
    [ObservableProperty] public partial string LoaderSummary { get; set; } = "Planning only — no loader binaries are downloaded or executed.";

    public AppTheme[] Themes { get; } = Enum.GetValues<AppTheme>();
    public string DataRoot => paths.DataRoot;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var current = settings.Current;
        SteamPath = current.SteamPath;
        CacheDirectory = current.CacheDirectory;
        Theme = current.Theme;
        AnimationsEnabled = current.AnimationsEnabled;
        AutoScrollLogs = current.AutoScrollLogs;
        RememberWindowSize = current.RememberWindowSize;
        DebugLogging = current.DebugLogging;
        FetchMetadata = current.FetchMetadata;
        await RefreshCacheAsync();
    }

    [RelayCommand]
    private async Task BrowseCacheAsync()
    {
        var selected = await picker.PickFolderAsync("Choose the artwork cache folder");
        if (selected is not null)
        {
            CacheDirectory = selected;
        }
    }

    [RelayCommand]
    private async Task BrowseSteamAsync()
    {
        var selected = await picker.PickFolderAsync("Choose the Steam installation folder");
        if (selected is not null)
        {
            SteamPath = selected;
            await VerifySteamAsync();
        }
    }

    [RelayCommand]
    private async Task DetectSteamAsync()
    {
        var result = await steam.DetectAsync(SteamPath);
        if (result.IsValid)
        {
            SteamPath = result.Path;
        }

        SetStatus(result.Message, !result.IsValid);
    }

    [RelayCommand]
    private async Task VerifySteamAsync()
    {
        var result = await steam.VerifyAsync(SteamPath);
        if (result.IsValid)
        {
            SteamPath = result.Path;
        }

        SetStatus(result.Message, !result.IsValid);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var current = settings.Current with
        {
            SteamPath = SteamPath.Trim(),
            CacheDirectory = string.IsNullOrWhiteSpace(CacheDirectory) ? paths.CoversRoot : CacheDirectory.Trim(),
            Theme = Theme,
            AnimationsEnabled = AnimationsEnabled,
            AutoScrollLogs = AutoScrollLogs,
            RememberWindowSize = RememberWindowSize,
            DebugLogging = DebugLogging,
            FetchMetadata = FetchMetadata
        };
        await settings.SaveAsync(current);
        logStore.MinimumLevel = DebugLogging
            ? Microsoft.Extensions.Logging.LogLevel.Debug
            : Microsoft.Extensions.Logging.LogLevel.Information;
        themes.Apply(Theme);
        SetStatus("Settings saved.");
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (!await dialogs.ConfirmAsync("Clear artwork cache?", "Downloaded covers and missing markers will be removed.", "Clear cache"))
        {
            return;
        }

        var count = await covers.ClearArtworkAsync();
        await RefreshCacheAsync();
        SetStatus($"Removed {count} cached artwork item(s).");
    }

    [RelayCommand]
    private async Task ShowLoaderPlanAsync()
    {
        var plan = loader.CreatePlan(SteamPath);
        LoaderSummary = string.Join(Environment.NewLine, plan.PlannedActions.Concat(plan.Warnings));
        await dialogs.ShowAsync("Loader safety plan", LoaderSummary);
    }

    private async Task RefreshCacheAsync()
    {
        var cache = await covers.GetStatisticsAsync();
        CacheSummary = $"{cache.ArtworkFiles} covers · {cache.TotalBytes / 1_048_576d:F1} MB · {cache.MissingMarkers} missing markers";
    }
}
