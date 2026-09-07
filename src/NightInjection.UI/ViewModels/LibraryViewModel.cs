using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.UI.Services;

namespace NightInjection.UI.ViewModels;

public sealed partial class LibraryViewModel(
    ISettingsService settings,
    ILibraryService library,
    IInjectionService injection,
    IDialogService dialogs) : ViewModelBase
{
    private IReadOnlyList<LibraryItem> _allItems = [];
    public ObservableCollection<LibraryItem> Items { get; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SortMode { get; set; } = "Recently processed";

    public string[] SortModes { get; } = ["Recently processed", "Name", "AppID"];

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSortModeChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync() => await RefreshCoreAsync(force: false);

    [RelayCommand]
    private async Task ForceRefreshAsync() => await RefreshCoreAsync(force: true);

    private async Task RefreshCoreAsync(bool force)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SetStatus("Reading the Steam library…");
        try
        {
            _allItems = await library.GetLibraryAsync(
                settings.Current.SteamPath,
                settings.Current.FetchMetadata,
                force);
            ApplyFilter();
            SetStatus($"{_allItems.Count} item(s) found.");
        }
        catch (Exception exception)
        {
            SetStatus($"Library refresh failed: {exception.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(LibraryItem? item)
    {
        if (item is null || !await dialogs.ConfirmAsync(
                "Remove AppID files?",
                $"Managed Lua and manifest files for {item.Name} ({item.AppId}) will be removed.",
                "Remove"))
        {
            return;
        }

        var result = await injection.RemoveAppIdAsync(settings.Current.SteamPath, item.AppId);
        SetStatus(result.Message, !result.Succeeded);
        if (result.Succeeded)
        {
            await RefreshCoreAsync(force: true);
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<LibraryItem> query = _allItems;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(item => item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || item.AppId.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        query = SortMode switch
        {
            "Name" => query.OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "AppID" => query.OrderBy(static item => item.AppId, StringComparer.Ordinal),
            _ => query.OrderByDescending(static item => item.LastProcessed)
        };

        Items.Clear();
        foreach (var item in query)
        {
            Items.Add(item);
        }
    }
}
