namespace NightInjection.Core.Models;

public sealed record LibraryItem(
    string AppId,
    string Name,
    string CoverPath,
    DateTimeOffset LastProcessed,
    string Status,
    bool NeedsSteamRestart);

public sealed record SteamMetadata(
    string AppId,
    string Name,
    string Type,
    bool IsOffline,
    DateTimeOffset CheckedAt);

public sealed record CacheStatistics(
    int ArtworkFiles,
    long TotalBytes,
    int MissingMarkers,
    DateTimeOffset? LastUpdated);
