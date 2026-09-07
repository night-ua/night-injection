using System.Collections.Concurrent;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Validation;

namespace NightInjection.Infrastructure.Steam;

public sealed class LibraryService(
    ISteamService steamService,
    ISteamMetadataService metadataService,
    ICoverService coverService,
    IHistoryRepository historyRepository,
    TimeProvider timeProvider) : ILibraryService
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IReadOnlyList<LibraryItem>? _cached;
    private string? _cachedPath;
    private bool _cachedWithMetadata;
    private DateTimeOffset _cachedAt;

    public async Task<IReadOnlyList<LibraryItem>> GetLibraryAsync(
        string steamPath,
        bool fetchMetadata,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceRefresh
            && _cached is not null
            && string.Equals(_cachedPath, steamPath, StringComparison.OrdinalIgnoreCase)
            && _cachedWithMetadata == fetchMetadata
            && timeProvider.GetUtcNow() - _cachedAt < TimeSpan.FromSeconds(30))
        {
            return _cached;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var verification = await steamService.VerifyAsync(steamPath, cancellationToken).ConfigureAwait(false);
            if (!verification.IsValid)
            {
                return [];
            }

            var directories = steamService.GetDirectories(verification.Path);
            if (!Directory.Exists(directories.Lua))
            {
                return [];
            }

            var latestHistory = (await historyRepository.ListAsync(
                    new HistoryQuery(Limit: 2_000),
                    cancellationToken).ConfigureAwait(false))
                .Where(static record => !string.IsNullOrWhiteSpace(record.AppId))
                .GroupBy(static record => record.AppId!, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
            var sourceFiles = Directory.EnumerateFiles(directories.Lua, "*.lua", SearchOption.TopDirectoryOnly)
                .Where(path => AppIdValidator.IsValid(Path.GetFileNameWithoutExtension(path)))
                .ToArray();
            var bag = new ConcurrentBag<LibraryItem>();

            await Parallel.ForEachAsync(
                sourceFiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 6,
                    CancellationToken = cancellationToken
                },
                async (source, token) =>
                {
                    var appId = Path.GetFileNameWithoutExtension(source);
                    var localCover = FindLocalCover(Path.Combine(directories.LibraryCache, appId));
                    var metadata = fetchMetadata
                        ? await metadataService.GetAsync(appId, token).ConfigureAwait(false)
                        : new SteamMetadata(appId, $"Game {appId}", "game", true, timeProvider.GetUtcNow());
                    var cover = fetchMetadata
                        ? await coverService.GetAsync(appId, localCover, cancellationToken: token).ConfigureAwait(false)
                        : localCover ?? coverService.PlaceholderPath;
                    var lastProcessed = new DateTimeOffset(File.GetLastWriteTimeUtc(source), TimeSpan.Zero);
                    var status = latestHistory.TryGetValue(appId, out var record)
                        ? record.Result
                        : "Ready";
                    bag.Add(new LibraryItem(
                        appId,
                        metadata.Name,
                        cover,
                        lastProcessed,
                        status,
                        localCover is null));
                }).ConfigureAwait(false);

            _cached = bag.OrderByDescending(static item => item.LastProcessed).ToArray();
            _cachedPath = verification.Path;
            _cachedWithMetadata = fetchMetadata;
            _cachedAt = timeProvider.GetUtcNow();
            return _cached;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static string? FindLocalCover(string root)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(static file =>
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    var extension = Path.GetExtension(file);
                    return (name.Contains("library_600x900", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("library_capsule", StringComparison.OrdinalIgnoreCase))
                        && extension is not null
                        && new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" }
                            .Contains(extension, StringComparer.OrdinalIgnoreCase);
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
