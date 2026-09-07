using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.UI.Services;

namespace NightInjection.UI.ViewModels;

public sealed partial class LogsViewModel : ViewModelBase
{
    private readonly IAppLogStore _store;
    private readonly IClipboardService _clipboard;
    private readonly IFilePickerService _picker;
    private IReadOnlyList<AppLogEntry> _snapshot = [];

    public LogsViewModel(
        IAppLogStore store,
        IClipboardService clipboard,
        IFilePickerService picker,
        ISettingsService settings)
    {
        _store = store;
        _clipboard = clipboard;
        _picker = picker;
        AutoScroll = settings.Current.AutoScrollLogs;
        _store.EntryAdded += OnEntryAdded;
    }

    public ObservableCollection<AppLogEntry> Entries { get; } = [];
    public string[] LevelFilters { get; } = ["All levels", "Debug", "Information", "Warning", "Error", "Critical"];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LevelFilter { get; set; } = "All levels";

    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnLevelFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Refresh()
    {
        _snapshot = _store.Snapshot();
        ApplyFilter();
        SetStatus($"{Entries.Count} visible log entries.");
    }

    [RelayCommand]
    private void Copy(AppLogEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _clipboard.SetText(Format(entry, includeException: false));
        SetStatus("Log entry copied.");
    }

    [RelayCommand]
    private void Clear()
    {
        _store.Clear();
        Refresh();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var text = string.Join(Environment.NewLine, _snapshot.Select(entry => Format(entry, includeException: true)));
        var path = await _picker.SaveTextAsync("night-injection-log.txt", ".txt", text);
        if (path is not null)
        {
            SetStatus($"Logs exported to {path}");
        }
    }

    private void OnEntryAdded(object? sender, AppLogEntry entry)
    {
        App.Window?.DispatcherQueue.TryEnqueue(Refresh);
    }

    private void ApplyFilter()
    {
        IEnumerable<AppLogEntry> query = _snapshot;
        if (LevelFilter != "All levels" && Enum.TryParse<LogLevel>(LevelFilter, out var level))
        {
            query = query.Where(entry => entry.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(entry => entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || entry.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        Entries.Clear();
        foreach (var entry in query.OrderBy(static entry => entry.Timestamp))
        {
            Entries.Add(entry);
        }
    }

    private static string Format(AppLogEntry entry, bool includeException)
    {
        var builder = new StringBuilder()
            .Append(entry.Timestamp.ToString("O")).Append(" [").Append(entry.Level).Append("] ")
            .Append(entry.Category).Append(": ").Append(entry.Message);
        if (includeException && !string.IsNullOrWhiteSpace(entry.ExceptionDetail))
        {
            builder.AppendLine().Append(entry.ExceptionDetail);
        }

        return builder.ToString();
    }
}
