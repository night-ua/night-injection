using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Validation;

namespace NightInjection.Infrastructure.Networking;

public sealed partial class CoverService(
    HttpClient httpClient,
    ISettingsService settings,
    IAppPathService paths,
    TimeProvider timeProvider,
    ILogger<CoverService> logger) : ICoverService
{
    private const long MaximumImageBytes = 8L * 1024 * 1024;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public string PlaceholderPath
    {
        get
        {
            var packaged = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.jpg");
            return File.Exists(packaged) ? packaged : string.Empty;
        }
    }

    public async Task<string> GetAsync(
        string appId,
        string? localSource = null,
        IReadOnlyList<Uri>? extraCandidates = null,
        CancellationToken cancellationToken = default)
    {
        var local = ResolveLocalPath(localSource);
        if (local is not null && await IsValidImageAsync(local, cancellationToken).ConfigureAwait(false))
        {
            return local;
        }

        if (!AppIdValidator.IsValid(appId))
        {
            return PlaceholderPath;
        }

        var cacheDirectory = CacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
        var cached = Path.Combine(cacheDirectory, $"{appId}.jpg");
        var missing = Path.Combine(cacheDirectory, $"{appId}.missing");
        if (await IsValidImageAsync(cached, cancellationToken).ConfigureAwait(false))
        {
            return cached;
        }

        if (File.Exists(missing)
            && timeProvider.GetUtcNow() - File.GetLastWriteTimeUtc(missing) < TimeSpan.FromHours(6))
        {
            return PlaceholderPath;
        }

        var gate = _locks.GetOrAdd(appId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await IsValidImageAsync(cached, cancellationToken).ConfigureAwait(false))
            {
                return cached;
            }

            var candidates = SteamCandidates(appId)
                .Concat(extraCandidates ?? [])
                .Distinct()
                .ToArray();
            foreach (var candidate in candidates)
            {
                if (candidate.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                if (await DownloadImageAsync(candidate, cached, cancellationToken).ConfigureAwait(false))
                {
                    File.Delete(missing);
                    return cached;
                }
            }

            await File.WriteAllTextAsync(
                missing,
                timeProvider.GetUtcNow().ToString("O"),
                cancellationToken).ConfigureAwait(false);
            return PlaceholderPath;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cache = CacheDirectory;
        if (!Directory.Exists(cache))
        {
            return Task.FromResult(new CacheStatistics(0, 0, 0, null));
        }

        var files = Directory.EnumerateFiles(cache, "*", SearchOption.TopDirectoryOnly)
            .Select(static path => new FileInfo(path))
            .ToArray();
        var artwork = files.Where(static file =>
            file.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)).ToArray();
        var markers = files.Count(static file => file.Extension.Equals(".missing", StringComparison.OrdinalIgnoreCase));
        var lastUpdated = files.Length == 0
            ? (DateTimeOffset?)null
            : files.Max(static file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
        return Task.FromResult(new CacheStatistics(
            artwork.Length,
            artwork.Sum(static file => file.Length),
            markers,
            lastUpdated));
    }

    public Task<int> ClearArtworkAsync(CancellationToken cancellationToken = default)
    {
        var removed = 0;
        if (!Directory.Exists(CacheDirectory))
        {
            return Task.FromResult(removed);
        }

        foreach (var file in Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.TopDirectoryOnly)
                     .Where(static file => IsArtworkCacheFile(Path.GetExtension(file))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogCacheDeleteFailed(logger, file, exception.Message);
            }
        }

        return Task.FromResult(removed);
    }

    private static bool IsArtworkCacheFile(string extension) => extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".missing", StringComparison.OrdinalIgnoreCase);

    private string CacheDirectory => Path.GetFullPath(
        string.IsNullOrWhiteSpace(settings.Current.CacheDirectory)
            ? paths.CoversRoot
            : settings.Current.CacheDirectory);

    private async Task<bool> DownloadImageAsync(
        Uri uri,
        string destination,
        CancellationToken cancellationToken)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Project-Lightning");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true
                || response.Content.Headers.ContentLength is > MaximumImageBytes)
            {
                return false;
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous);
            var buffer = new byte[81_920];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > MaximumImageBytes)
                {
                    return false;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Close();
            if (!await IsValidImageAsync(temporary, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                             or IOException
                                             or UnauthorizedAccessException)
        {
            LogArtworkUnavailable(logger, uri, exception.Message);
            return false;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static IReadOnlyList<Uri> SteamCandidates(string appId) =>
    [
        new($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg"),
        new($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg"),
        new($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_capsule.jpg"),
        new($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg"),
        new($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg")
    ];

    private static string? ResolveLocalPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.IsFile ? uri.LocalPath : null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static async Task<bool> IsValidImageAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var header = new byte[16];
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
            return read >= 4
                && (header.AsSpan(0, 3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })
                    || header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
                    || header.AsSpan(0, 3).SequenceEqual("GIF"u8)
                    || header.AsSpan(0, 2).SequenceEqual("BM"u8)
                    || (read >= 12
                        && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                        && header.AsSpan(8, 4).SequenceEqual("WEBP"u8)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(LogLevel.Information, "Artwork unavailable from {Uri}: {Reason}")]
    private static partial void LogArtworkUnavailable(ILogger logger, Uri uri, string reason);

    [LoggerMessage(LogLevel.Warning, "Could not remove cache file {Path}: {Reason}")]
    private static partial void LogCacheDeleteFailed(ILogger logger, string path, string reason);
}
