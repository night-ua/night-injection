using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;

namespace NightInjection.Infrastructure.Networking;

public sealed partial class SteamMetadataService(
    HttpClient httpClient,
    ISettingsService settings,
    IAppPathService paths,
    TimeProvider timeProvider,
    ILogger<SteamMetadataService> logger) : ISteamMetadataService
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly ConcurrentDictionary<string, MetadataCacheEntry> _cache = new(StringComparer.Ordinal);
    private volatile bool _loaded;

    public async Task<SteamMetadata> GetAsync(string appId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        if (_cache.TryGetValue(appId, out var cached)
            && (!cached.Offline || now - cached.CheckedAt < TimeSpan.FromHours(1)))
        {
            return cached.ToModel(appId);
        }

        try
        {
            var uri = new Uri(
                $"https://store.steampowered.com/api/appdetails?appids={Uri.EscapeDataString(appId)}&l=english");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Project-Lightning");
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty(appId, out var node)
                || !node.TryGetProperty("success", out var success)
                || !success.GetBoolean()
                || !node.TryGetProperty("data", out var data))
            {
                throw new InvalidDataException("Steam returned no game metadata.");
            }

            var name = data.TryGetProperty("name", out var nameNode)
                ? nameNode.GetString()
                : null;
            var type = data.TryGetProperty("type", out var typeNode)
                ? typeNode.GetString()
                : null;
            var result = new MetadataCacheEntry(
                string.IsNullOrWhiteSpace(name) ? $"Game {appId}" : name,
                string.IsNullOrWhiteSpace(type) ? "game" : type,
                false,
                now);
            _cache[appId] = result;
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return result.ToModel(appId);
        }
        catch (Exception exception) when (exception is HttpRequestException
                                             or IOException
                                             or JsonException
                                             or InvalidDataException)
        {
            LogMetadataUnavailable(logger, appId, exception.Message);
            var fallback = new MetadataCacheEntry(
                cached?.Name ?? $"Game {appId}",
                cached?.Type ?? "game",
                true,
                now);
            _cache[appId] = fallback;
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return fallback.ToModel(appId);
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var path = MetadataPath;
            if (File.Exists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var item in document.RootElement.EnumerateObject())
                    {
                        if (item.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var name = ReadString(item.Value, "name") ?? $"Game {item.Name}";
                        var type = ReadString(item.Value, "type") ?? "game";
                        var offline = ReadBoolean(item.Value, "offline");
                        var checkedAt = ReadTimestamp(item.Value, "checked_at") ?? DateTimeOffset.UnixEpoch;
                        _cache[item.Name] = new MetadataCacheEntry(name, type, offline, checkedAt);
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    LogMetadataCacheUnreadable(logger, exception.Message);
                }
            }

            _loaded = true;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = $"{MetadataPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MetadataPath)!);
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    _cache,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, MetadataPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogMetadataCacheWriteFailed(logger, exception.Message);
        }
        finally
        {
            TryDelete(temporary);
            _saveGate.Release();
        }
    }

    private string MetadataPath
    {
        get
        {
            var coverDirectory = string.IsNullOrWhiteSpace(settings.Current.CacheDirectory)
                ? paths.CoversRoot
                : settings.Current.CacheDirectory;
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(coverDirectory)) ?? paths.CacheRoot, "steam_metadata.json");
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var floatingSeconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(floatingSeconds * 1000));
        }

        return value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var timestamp)
                    ? timestamp
                    : null;
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

    private sealed record MetadataCacheEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("offline")] bool Offline,
        [property: JsonPropertyName("checked_at")] DateTimeOffset CheckedAt)
    {
        public SteamMetadata ToModel(string appId) => new(appId, Name, Type, Offline, CheckedAt);
    }

    [LoggerMessage(LogLevel.Information, "Steam metadata unavailable for AppID {AppId}: {Reason}")]
    private static partial void LogMetadataUnavailable(ILogger logger, string appId, string reason);

    [LoggerMessage(LogLevel.Warning, "Steam metadata cache could not be read: {Reason}")]
    private static partial void LogMetadataCacheUnreadable(ILogger logger, string reason);

    [LoggerMessage(LogLevel.Warning, "Steam metadata cache could not be saved: {Reason}")]
    private static partial void LogMetadataCacheWriteFailed(ILogger logger, string reason);
}
