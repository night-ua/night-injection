using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Models;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Steam;
using NightInjection.Infrastructure.Storage;

namespace NightInjection.Tests;

public sealed class InjectionServiceTests
{
    [Fact]
    public async Task DefaultPluginPlanningIsADryOperationAndMapsLuaToPluginFolder()
    {
        using var environment = new TestEnvironment();
        var source = environment.CreateFile("input/220.lua", "addappid(220)");
        var service = CreateService(environment, out _);

        var plan = await service.BuildFilePlanAsync([source], environment.Steam);

        Assert.True(plan.IsValid);
        var entry = Assert.Single(plan.Entries);
        Assert.EndsWith("config\\stplug-in\\220.lua", entry.DestinationPath, StringComparison.OrdinalIgnoreCase);
        Assert.All(plan.Entries, entry => Assert.False(File.Exists(entry.DestinationPath)));
    }

    [Theory]
    [InlineData(LuaInjectionTarget.Plugin, 1, true, false)]
    [InlineData(LuaInjectionTarget.Lua, 1, false, true)]
    [InlineData(LuaInjectionTarget.Both, 2, true, true)]
    public async Task PlanningUsesConfiguredLuaTarget(
        LuaInjectionTarget target,
        int expectedCount,
        bool expectsPlugin,
        bool expectsLua)
    {
        using var environment = new TestEnvironment();
        var source = environment.CreateFile("input/730.lua", "addappid(730)");
        var service = CreateService(environment, out _, target);

        var plan = await service.BuildFilePlanAsync([source], environment.Steam);

        Assert.True(plan.IsValid);
        Assert.Equal(expectedCount, plan.Entries.Count);
        Assert.Equal(expectsPlugin, plan.Entries.Any(entry => entry.DestinationPath.EndsWith("config\\stplug-in\\730.lua", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(expectsLua, plan.Entries.Any(entry => entry.DestinationPath.EndsWith("config\\lua\\730.lua", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ExecuteCommitsAllFilesAndAddsHistory()
    {
        using var environment = new TestEnvironment();
        var lua = environment.CreateFile("input/440.lua", "lua-data");
        var manifest = environment.CreateFile("input/440.manifest", "manifest-data");
        var service = CreateService(environment, out var history);
        var plan = await service.BuildFilePlanAsync([lua, manifest], environment.Steam);

        var result = await service.ExecutePlanAsync(plan);

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.AffectedPaths.Count);
        Assert.All(result.AffectedPaths, path => Assert.True(File.Exists(path)));
        Assert.Equal("lua-data", File.ReadAllText(Path.Combine(environment.Steam, "config", "stplug-in", "440.lua")));
        Assert.Equal("manifest-data", File.ReadAllText(Path.Combine(environment.Steam, "config", "depotcache", "440.manifest")));
        var records = await history.ListAsync();
        Assert.Single(records);
        Assert.Equal("Inject", records[0].Operation);
        Assert.Equal("Success", records[0].Result);
    }

    [Fact]
    public async Task LuaExecutionOverwritesBothSteamCopiesWithIdenticalBytes()
    {
        using var environment = new TestEnvironment();
        var source = environment.CreateFile("input/custom.lua", "addappid(730)\r\n-- UTF-8: ليل");
        var pluginTarget = environment.CreateFile("Steam/config/stplug-in/custom.lua", "old plugin");
        var luaTarget = environment.CreateFile("Steam/config/lua/custom.lua", "old lua");
        var expected = await File.ReadAllBytesAsync(source);
        var service = CreateService(environment, out _, LuaInjectionTarget.Both);

        var plan = await service.BuildFilePlanAsync([source], environment.Steam);
        var result = await service.ExecutePlanAsync(plan);

        Assert.True(plan.IsValid);
        Assert.Equal(2, plan.OverwriteCount);
        Assert.True(result.Succeeded);
        Assert.Equal(expected, await File.ReadAllBytesAsync(pluginTarget));
        Assert.Equal(expected, await File.ReadAllBytesAsync(luaTarget));
    }

    [Fact]
    public async Task DuplicateDestinationNamesInvalidatePlan()
    {
        using var environment = new TestEnvironment();
        var first = environment.CreateFile("a/10.lua", "one");
        var second = environment.CreateFile("b/10.lua", "two");
        var service = CreateService(environment, out _);

        var plan = await service.BuildFilePlanAsync([first, second], environment.Steam);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, error => error.Contains("collision", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteRejectsDestinationOutsideSteamAllowedFolders()
    {
        using var environment = new TestEnvironment();
        var source = environment.CreateFile("input/20.lua", "safe");
        var service = CreateService(environment, out _);
        var outside = Path.Combine(environment.Root, "outside.lua");
        var plan = new InjectionPlan
        {
            SourceKind = InjectionSourceKind.LocalFiles,
            SteamPath = environment.Steam,
            Entries = [new InjectionPlanEntry(source, "20.lua", outside, InjectionFileType.Lua, 4, false)]
        };

        var result = await service.ExecutePlanAsync(plan);

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task InvalidRemoveAppIdReturnsFailureWithoutTouchingFiles()
    {
        using var environment = new TestEnvironment();
        var service = CreateService(environment, out _);

        var result = await service.RemoveAppIdAsync(environment.Steam, "../220");

        Assert.False(result.Succeeded);
        Assert.Empty(result.AffectedPaths);
    }

    [Fact]
    public async Task RemoveAppIdRemovesLuaFromBothFoldersAndManifest()
    {
        using var environment = new TestEnvironment();
        var plugin = environment.CreateFile("Steam/config/stplug-in/220.lua");
        var lua = environment.CreateFile("Steam/config/lua/220.lua");
        var manifest = environment.CreateFile("Steam/config/depotcache/220.manifest");
        var service = CreateService(environment, out _);

        var result = await service.RemoveAppIdAsync(environment.Steam, "220");

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.AffectedPaths.Count);
        Assert.False(File.Exists(plugin));
        Assert.False(File.Exists(lua));
        Assert.False(File.Exists(manifest));
    }

    [Fact]
    public async Task ZipPlanningCleansOnlyOwnedTemporaryPlan()
    {
        using var environment = new TestEnvironment();
        var zipPath = Path.Combine(environment.Root, "bundle.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("nested/30.lua");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("addappid(30)");
        }

        var service = CreateService(environment, out _, LuaInjectionTarget.Lua);
        var plan = await service.BuildFilePlanAsync([zipPath], environment.Steam);
        Assert.True(plan.IsValid);
        var plannedEntry = Assert.Single(plan.Entries);
        Assert.EndsWith("config\\lua\\30.lua", plannedEntry.DestinationPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(plan.TemporaryDirectory);
        Assert.True(Directory.Exists(plan.TemporaryDirectory));

        await service.CleanupPlanAsync(plan);

        Assert.False(Directory.Exists(plan.TemporaryDirectory));
        Assert.True(Directory.Exists(environment.Temp));
    }

    [Fact]
    public async Task FailedSecondCommitRollsBackFirstCommittedFile()
    {
        using var environment = new TestEnvironment();
        var firstSource = environment.CreateFile("input/first.lua", "new");
        var secondSource = environment.CreateFile("input/second.lua", "blocked");
        var firstDestination = environment.CreateFile("Steam/config/stplug-in/first.lua", "original");
        var secondDestination = Path.Combine(environment.Steam, "config", "lua", "second.lua");
        Directory.CreateDirectory(secondDestination);
        var service = CreateService(environment, out _);
        var plan = new InjectionPlan
        {
            SteamPath = environment.Steam,
            Entries =
            [
                new(firstSource, "first.lua", firstDestination, InjectionFileType.Lua, 3, true),
                new(secondSource, "second.lua", secondDestination, InjectionFileType.Lua, 7, false)
            ]
        };

        var result = await service.ExecutePlanAsync(plan);

        Assert.False(result.Succeeded);
        Assert.Equal("original", await File.ReadAllTextAsync(firstDestination));
    }

    private static InjectionService CreateService(
        TestEnvironment environment,
        out HistoryRepository history,
        LuaInjectionTarget target = LuaInjectionTarget.Plugin)
    {
        history = new HistoryRepository(environment.Paths, NullLogger<HistoryRepository>.Instance);
        return new InjectionService(
            environment.Paths,
            new TestSettingsService(new AppSettings { LuaInjectionTarget = target }),
            new SteamService(),
            history,
            new SafeZipExtractor(),
            TimeProvider.System,
            NullLogger<InjectionService>.Instance);
    }
}
