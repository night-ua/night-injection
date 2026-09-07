using NightInjection.Core.Models;
using NightInjection.Core.Results;
using NightInjection.Core.Validation;
using Microsoft.Extensions.Logging;

namespace NightInjection.Core.Interfaces;

public interface IAppPathService
{
    string DataRoot { get; }
    string CacheRoot { get; }
    string CoversRoot { get; }
    string LogsRoot { get; }
    string DatabasePath { get; }
    string SettingsPath { get; }
    string TemporaryRoot { get; }
    void EnsureApplicationDirectories();
}

public interface ISettingsService
{
    AppSettings Current { get; }
    Task<AppSettings> InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ISteamService
{
    Task<SteamInstallationStatus> DetectAsync(string? savedPath, CancellationToken cancellationToken = default);
    Task<SteamInstallationStatus> VerifyAsync(string? path, CancellationToken cancellationToken = default);
    SteamDirectories GetDirectories(string steamPath);
}

public interface IInjectionService
{
    Task<InjectionPlan> BuildFilePlanAsync(
        IReadOnlyCollection<string> files,
        string steamPath,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ExecutePlanAsync(
        InjectionPlan plan,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveAppIdAsync(
        string steamPath,
        string appId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> ClearPluginsAsync(
        string steamPath,
        CancellationToken cancellationToken = default);

    Task CleanupPlanAsync(InjectionPlan plan);
}

public interface IAppIdService
{
    Task<InjectionPlan> BuildPlanAsync(
        string steamPath,
        string appId,
        bool force,
        CancellationToken cancellationToken = default);
}

public interface IWebsiteImportService
{
    Task<WebsiteImportResult> DownloadAsync(
        WebsiteImportRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILibraryService
{
    Task<IReadOnlyList<LibraryItem>> GetLibraryAsync(
        string steamPath,
        bool fetchMetadata,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);
}

public interface ISteamMetadataService
{
    Task<SteamMetadata> GetAsync(string appId, CancellationToken cancellationToken = default);
}

public interface ICoverService
{
    string PlaceholderPath { get; }
    Task<string> GetAsync(
        string appId,
        string? localSource = null,
        IReadOnlyList<Uri>? extraCandidates = null,
        CancellationToken cancellationToken = default);
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task<int> ClearArtworkAsync(CancellationToken cancellationToken = default);
}

public interface IHistoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(NewHistoryRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoryRecord>> ListAsync(
        HistoryQuery? query = null,
        CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IAppLogStore
{
    LogLevel MinimumLevel { get; set; }
    event EventHandler<AppLogEntry>? EntryAdded;
    IReadOnlyList<AppLogEntry> Snapshot();
    void Clear();
}

public interface ILoaderPlanService
{
    LoaderPlan CreatePlan(string steamPath);
}
