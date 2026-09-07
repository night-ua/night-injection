using System.Net;
using System.Text;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Models;
using NightInjection.Infrastructure.Configuration;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Networking;
using NightInjection.Infrastructure.Steam;

namespace NightInjection.Tests;

public sealed class NetworkingAndCatalogTests
{
    [Fact]
    public async Task MetadataParsesSteamResponseAndCachesIt()
    {
        using var environment = new TestEnvironment();
        var settings = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await settings.InitializeAsync();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"220\":{\"success\":true,\"data\":{\"name\":\"Half-Life 2\",\"type\":\"game\"}}}", Encoding.UTF8, "application/json")
        });
        var service = new SteamMetadataService(
            new HttpClient(handler), settings, environment.Paths, TimeProvider.System,
            NullLogger<SteamMetadataService>.Instance);

        var first = await service.GetAsync("220");
        var second = await service.GetAsync("220");

        Assert.Equal("Half-Life 2", first.Name);
        Assert.False(first.IsOffline);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.CallCount);
        Assert.True(File.Exists(Path.Combine(environment.Paths.CacheRoot, "steam_metadata.json")));

        var offlineHandler = new StubHttpHandler(_ => throw new HttpRequestException("must not be called"));
        var reloaded = new SteamMetadataService(
            new HttpClient(offlineHandler), settings, environment.Paths, TimeProvider.System,
            NullLogger<SteamMetadataService>.Instance);
        var fromDisk = await reloaded.GetAsync("220");
        Assert.Equal("Half-Life 2", fromDisk.Name);
        Assert.Equal(0, offlineHandler.CallCount);
    }

    [Fact]
    public async Task MetadataUsesPredictableOfflineFallback()
    {
        using var environment = new TestEnvironment();
        var settings = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await settings.InitializeAsync();
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("offline"));
        var service = new SteamMetadataService(
            new HttpClient(handler), settings, environment.Paths, TimeProvider.System,
            NullLogger<SteamMetadataService>.Instance);

        var result = await service.GetAsync("999");

        Assert.Equal("Game 999", result.Name);
        Assert.True(result.IsOffline);
    }

    [Fact]
    public async Task InvalidAppIdNeverTouchesNetwork()
    {
        using var environment = new TestEnvironment();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new AppIdService(
            new HttpClient(handler), environment.Paths, new SteamService(), new SafeZipExtractor(),
            NullLogger<AppIdService>.Instance);

        var plan = await service.BuildPlanAsync(environment.Steam, "../bad", false);

        Assert.False(plan.IsValid);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AppIdLookupHonorsRepositoryOrderAndTransformsSpinLua()
    {
        using var environment = new TestEnvironment();
        var archiveBytes = CreateZipBytes(
            ("SB_manifest_DB-220/220.lua", "addappid(220)\nsetManifestid(220, 1)\nprint('drop')"),
            ("SB_manifest_DB-220/220.manifest", "ignored"));
        var handler = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("ProjectLightningManifests", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archiveBytes)
            };
        });
        var service = new AppIdService(
            new HttpClient(handler), environment.Paths, new SteamService(), new SafeZipExtractor(),
            NullLogger<AppIdService>.Instance);

        var plan = await service.BuildPlanAsync(environment.Steam, "220", force: false);

        Assert.True(plan.IsValid);
        Assert.Contains("SPIN0ZAi", plan.Repository, StringComparison.Ordinal);
        Assert.Equal(2, plan.Entries.Count);
        Assert.All(plan.Entries, entry => Assert.Equal(InjectionFileType.Lua, entry.FileType));
        var transformed = await File.ReadAllTextAsync(plan.Entries[0].SourcePath);
        Assert.Contains("addappid(220)", transformed, StringComparison.Ordinal);
        Assert.DoesNotContain("setManifestid", transformed, StringComparison.Ordinal);
        Assert.DoesNotContain("print", transformed, StringComparison.Ordinal);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CoverUsesValidLocalFileAndClearPreservesUnrelatedFiles()
    {
        using var environment = new TestEnvironment();
        var cache = Path.Combine(environment.Root, "chosen-cache");
        var settings = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await settings.SaveAsync(new AppSettings { CacheDirectory = cache });
        var local = Path.Combine(environment.Root, "local.png");
        await File.WriteAllBytesAsync(local, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = new CoverService(
            new HttpClient(handler), settings, environment.Paths, TimeProvider.System,
            NullLogger<CoverService>.Instance);

        var result = await service.GetAsync("220", local);
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "220.jpg"), "cache");
        await File.WriteAllTextAsync(Path.Combine(cache, "notes.txt"), "keep");
        var removed = await service.ClearArtworkAsync();

        Assert.Equal(local, result);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(1, removed);
        Assert.True(File.Exists(Path.Combine(cache, "notes.txt")));
    }

    [Fact]
    public async Task CoverRejectsPathLikeCatalogAppIdBeforeNetworkOrCacheAccess()
    {
        using var environment = new TestEnvironment();
        var settings = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await settings.InitializeAsync();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new CoverService(
            new HttpClient(handler), settings, environment.Paths, TimeProvider.System,
            NullLogger<CoverService>.Instance);

        await service.GetAsync("../../escape");

        Assert.Equal(0, handler.CallCount);
        Assert.False(File.Exists(Path.Combine(environment.Paths.CacheRoot, "escape.jpg")));
    }

    [Fact]
    public void LoaderServiceOnlyReturnsPlanAndDoesNotCreateFiles()
    {
        using var environment = new TestEnvironment();
        var plan = new LoaderPlanService(new SteamService()).CreatePlan(environment.Steam);

        Assert.True(plan.IsValid);
        Assert.Contains(plan.Warnings, warning => warning.Contains("does not download", StringComparison.OrdinalIgnoreCase));
        Assert.False(File.Exists(Path.Combine(environment.Steam, "dwmapi.dll")));
    }

    private static byte[] CreateZipBytes(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
