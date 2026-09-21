using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Infrastructure.Steam;
using NightInjection.Infrastructure.Storage;

namespace NightInjection.Tests;

public sealed class LibraryServiceTests
{
    [Fact]
    public async Task LibraryReadsBothFoldersDeduplicatesAppIdsAndPrefersPluginSource()
    {
        using var environment = new TestEnvironment();
        var history = new HistoryRepository(environment.Paths, NullLogger<HistoryRepository>.Instance);
        var service = new LibraryService(new SteamService(), new FakeMetadata(), new FakeCover(), history, TimeProvider.System);

        var empty = await service.GetLibraryAsync(environment.Steam, fetchMetadata: true);
        Assert.Empty(empty);
        Assert.False(Directory.Exists(Path.Combine(environment.Steam, "config", "lua")));
        Assert.False(Directory.Exists(Path.Combine(environment.Steam, "config", "stplug-in")));

        var luaCopy = environment.CreateFile("Steam/config/lua/220.lua");
        environment.CreateFile("Steam/config/lua/not-an-id.lua");
        var pluginCopy = environment.CreateFile("Steam/config/stplug-in/220.lua");
        environment.CreateFile("Steam/config/stplug-in/730.lua");
        File.SetLastWriteTimeUtc(luaCopy, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var preferredWriteTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(pluginCopy, preferredWriteTime);
        var items = await service.GetLibraryAsync(environment.Steam, fetchMetadata: true, forceRefresh: true);

        Assert.Equal(2, items.Count);
        var item = Assert.Single(items, item => item.AppId == "220");
        Assert.Equal("220", item.AppId);
        Assert.Equal("Title 220", item.Name);
        Assert.Equal("cover-220", item.CoverPath);
        Assert.Equal(new DateTimeOffset(preferredWriteTime), item.LastProcessed);
    }

    private sealed class FakeMetadata : ISteamMetadataService
    {
        public Task<SteamMetadata> GetAsync(string appId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SteamMetadata(appId, $"Title {appId}", "game", false, DateTimeOffset.UtcNow));
    }

    private sealed class FakeCover : ICoverService
    {
        public string PlaceholderPath => "placeholder";
        public Task<string> GetAsync(string appId, string? localSource = null, IReadOnlyList<Uri>? extraCandidates = null, CancellationToken cancellationToken = default) =>
            Task.FromResult($"cover-{appId}");
        public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new CacheStatistics(0, 0, 0, null));
        public Task<int> ClearArtworkAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
