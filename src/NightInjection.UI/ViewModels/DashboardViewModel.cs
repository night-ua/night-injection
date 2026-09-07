using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.UI.Messages;

namespace NightInjection.UI.ViewModels;

public sealed partial class DashboardViewModel(
    ISettingsService settings,
    ISteamService steam,
    ILibraryService library,
    IHistoryRepository history,
    ICoverService covers) : ViewModelBase
{
    public ObservableCollection<HistoryRecord> RecentOperations { get; } = [];

    public string SteamPath { get; private set; } = "Not configured";
    public string SteamStatus { get; private set; } = "Checking Steam…";
    public string CacheSummary { get; private set; } = "Not calculated";
    public int LibraryCount { get; private set; }
    public int ProcessedCount { get; private set; }

    [RelayCommand]
    private void Navigate(string? section)
    {
        if (!string.IsNullOrWhiteSpace(section))
        {
            WeakReferenceMessenger.Default.Send(new NavigationRequestMessage(section));
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SetStatus(string.Empty);
        try
        {
            var statusTask = steam.VerifyAsync(settings.Current.SteamPath);
            var historyTask = history.ListAsync(new HistoryQuery(Limit: 5_000));
            var cacheTask = covers.GetStatisticsAsync();
            var status = await statusTask;
            SteamPath = string.IsNullOrWhiteSpace(status.Path) ? "Not detected" : status.Path;
            SteamStatus = status.IsValid ? $"Ready · {status.DetectionSource}" : status.Message;
            OnPropertyChanged(nameof(SteamPath));
            OnPropertyChanged(nameof(SteamStatus));

            var records = await historyTask;
            RecentOperations.Clear();
            foreach (var record in records.Take(8))
            {
                RecentOperations.Add(record);
            }

            ProcessedCount = records.Count;
            OnPropertyChanged(nameof(ProcessedCount));

            if (status.IsValid)
            {
                LibraryCount = (await library.GetLibraryAsync(status.Path, fetchMetadata: false)).Count;
                OnPropertyChanged(nameof(LibraryCount));
            }

            var cache = await cacheTask;
            CacheSummary = $"{cache.ArtworkFiles:N0} covers · {FormatBytes(cache.TotalBytes)}";
            OnPropertyChanged(nameof(CacheSummary));
        }
        catch (Exception exception)
        {
            SetStatus($"Dashboard refresh failed: {exception.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576d:F1} MB",
        >= 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes} B"
    };
}
