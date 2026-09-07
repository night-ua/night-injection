using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.UI.Services;

namespace NightInjection.UI.ViewModels;

public sealed partial class HistoryViewModel(
    IHistoryRepository history,
    IFilePickerService picker,
    IDialogService dialogs) : ViewModelBase
{
    public ObservableCollection<HistoryRecord> Records { get; } = [];
    public string[] ResultFilters { get; } = ["All results", "Success", "Error"];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultFilter { get; set; } = "All results";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var result = ResultFilter == "All results" ? null : ResultFilter;
            var records = await history.ListAsync(new HistoryQuery(SearchText, Result: result, Limit: 1000));
            Records.Clear();
            foreach (var record in records)
            {
                Records.Add(record);
            }

            SetStatus($"{Records.Count} record(s).", false);
        }
        catch (Exception exception)
        {
            SetStatus($"History could not be loaded: {exception.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (!await dialogs.ConfirmAsync("Clear history?", "All operation history will be permanently removed.", "Clear history"))
        {
            return;
        }

        await history.ClearAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var builder = new StringBuilder("Timestamp,Operation,AppID,File,Result,Details,Repository,DryRun\r\n");
        foreach (var record in Records)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                Csv(record.Timestamp.ToString("O")), Csv(record.Operation), Csv(record.AppId),
                Csv(record.SourceFile), Csv(record.Result), Csv(record.Details), Csv(record.Repository),
                record.DryRun.ToString()
            }));
        }

        var path = await picker.SaveTextAsync("night-injection-history.csv", ".csv", builder.ToString());
        if (path is not null)
        {
            SetStatus($"History exported to {path}");
        }
    }

    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}
