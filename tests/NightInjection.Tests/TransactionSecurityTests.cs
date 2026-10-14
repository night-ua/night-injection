using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Steam;
using NightInjection.Infrastructure.Storage;

namespace NightInjection.Tests;

public sealed class TransactionSecurityTests
{
    [Fact]
    public async Task ConcurrentMutationsAreSerializedAcrossServiceInstances()
    {
        using var environment = new TestEnvironment();
        var source = environment.CreateFile("input/730.lua", "new");
        var plan = await Engine(environment).BuildFilePlanAsync([source], environment.Steam);
        var steam = new GateSteam();
        var history = new HistoryRepository(environment.Paths, NullLogger<HistoryRepository>.Instance);
        InjectionService NewEngine() => new(environment.Paths, new TestSettingsService(), steam, history,
            new SafeZipExtractor(), TimeProvider.System, NullLogger<InjectionService>.Instance);
        var first = NewEngine().ExecutePlanAsync(plan);
        Task<NightInjection.Core.Results.OperationResult>? second = null;
        try
        {
            await steam.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = NewEngine().ClearPluginsAsync(environment.Steam);
            Assert.Equal(1, steam.Calls);
        }
        finally { steam.Release.TrySetResult(); }
        Assert.All(await Task.WhenAll(first, second!), result => Assert.True(result.Succeeded, result.Message));
        Assert.Equal(2, steam.Calls);
    }
    [Fact]
    public async Task ChangedSourceIsRejectedBeforeCommit()
    {
        using var environment = new TestEnvironment();
        var source = environment.CreateFile("input/730.lua", "original");
        var engine = Engine(environment);
        var plan = await engine.BuildFilePlanAsync([source], environment.Steam);
        File.WriteAllText(source, "changed");
        var result = await engine.ExecutePlanAsync(plan);
        Assert.False(result.Succeeded);
        Assert.Contains("changed after preview", result.Message);
        Assert.False(File.Exists(plan.Entries[0].DestinationPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(plan.Entries[0].DestinationPath)!));
    }

    [Fact]
    public async Task CommitFailureRollsBackEarlierOverwrite()
    {
        using var environment = new TestEnvironment();
        var first = environment.CreateFile("input/730.lua", "new");
        var second = environment.CreateFile("input/220.lua", "second");
        var original = environment.CreateFile("Steam/config/stplug-in/730.lua", "old");
        var blocked = Path.Combine(environment.Steam, "config", "stplug-in", "220.lua");
        Directory.CreateDirectory(blocked);
        var plan = new InjectionPlan { SteamPath = environment.Steam, Entries = [
            new(first, "730.lua", original, InjectionFileType.Lua, 3, true),
            new(second, "220.lua", blocked, InjectionFileType.Lua, 6, false),
        ] };
        var result = await Engine(environment).ExecutePlanAsync(plan);
        Assert.False(result.Succeeded);
        Assert.Equal("old", File.ReadAllText(original));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(original)!, "*.tmp"));
    }

    private static InjectionService Engine(TestEnvironment e) => new(e.Paths, new TestSettingsService(),
        new SteamService(), new HistoryRepository(e.Paths, NullLogger<HistoryRepository>.Instance),
        new SafeZipExtractor(), TimeProvider.System, NullLogger<InjectionService>.Instance);

    private sealed class GateSteam : ISteamService
    {
        public int Calls;
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly SteamService _inner = new();
        public async Task<SteamInstallationStatus> VerifyAsync(string? path, CancellationToken token = default)
        {
            if (++Calls == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return await _inner.VerifyAsync(path, token);
        }
        public Task<SteamInstallationStatus> DetectAsync(string? path, CancellationToken token = default) => _inner.DetectAsync(path, token);
        public SteamDirectories GetDirectories(string path) => _inner.GetDirectories(path);
    }
}
