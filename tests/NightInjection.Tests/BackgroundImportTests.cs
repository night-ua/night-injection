using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Results;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Steam;
using NightInjection.Infrastructure.SteamBridge;
using NightInjection.Infrastructure.Storage;

namespace NightInjection.Tests;

public sealed class BackgroundImportTests
{
    private static BackgroundImportCoordinator Coordinator(TestEnvironment e, FakeDownloads downloads)
    {
        var settings = new TestSettingsService(new AppSettings { SteamPath = e.Steam });
        return new(downloads, new InjectionService(e.Paths, settings, new SteamService(),
            new HistoryRepository(e.Paths, NullLogger<HistoryRepository>.Instance), new SafeZipExtractor(),
            TimeProvider.System, NullLogger<InjectionService>.Instance), settings, e.Paths, new SteamService());
    }
    private static async Task<SteamImportSnapshot> Wait(BackgroundImportCoordinator worker, string id, params SteamImportState[] states)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var reply = await worker.HandleAsync(new("status", id), timeout.Token);
            if (reply.Task is { } task && states.Contains(task.State)) return task;
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task ManualModeRequiresConfirmationAndRejectsCrossGameOrDuplicateApply()
    {
        using var e = new TestEnvironment(); var downloads = new FakeDownloads(e);
        await using var worker = Coordinator(e, downloads); await worker.InitializeAsync();
        Assert.False(worker.Settings.AutomaticApply);
        Assert.False((await worker.HandleAsync(new("start", "../730"))).Ok);
        var first = await worker.HandleAsync(new("start", "730", GameName: "Mock"));
        var duplicate = await worker.HandleAsync(new("start", "730"));
        Assert.Equal(first.Task!.TaskId, duplicate.Task!.TaskId);
        var task = await Wait(worker, "730", SteamImportState.ReadyToApply);
        Assert.Equal(1, downloads.Calls); Assert.Equal(1, task.FileCount);
        var target = Assert.Single(task.Preview!).Destination;
        Assert.False(File.Exists(target));
        Assert.False((await worker.HandleAsync(new("apply", "220", task.TaskId, Confirmed: true))).Ok);
        Assert.Equal("CONSENT_REQUIRED", (await worker.HandleAsync(new("apply", "730", task.TaskId))).Error);
        Assert.True((await worker.HandleAsync(new("apply", "730", task.TaskId, Confirmed: true))).Ok);
        Assert.False((await worker.HandleAsync(new("apply", "730", task.TaskId, Confirmed: true))).Ok);
        await Wait(worker, "730", SteamImportState.Completed);
        Assert.Equal("addappid(730)", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task AutomaticApplyRequiresOptInAndDisablingItStopsFutureAutomaticWrites()
    {
        using var e = new TestEnvironment(); await using var worker = Coordinator(e, new FakeDownloads(e));
        await worker.InitializeAsync();
        var enabled = worker.Settings with { AutomaticApply = true, ColorMode = "custom", CustomColor = "#abc" };
        Assert.Equal("CONSENT_REQUIRED", (await worker.HandleAsync(new("settings", Settings: enabled))).Error);
        Assert.True((await worker.HandleAsync(new("settings", Confirmed: true, Settings: enabled))).Ok);
        await worker.HandleAsync(new("start", "730"));
        var completed = await Wait(worker, "730", SteamImportState.Completed);
        Assert.True(completed.AutomaticRequested);
        Assert.True(File.Exists(Assert.Single(completed.Preview!).Destination));
        await worker.HandleAsync(new("settings", Settings: worker.Settings with { AutomaticApply = false }));
        await worker.HandleAsync(new("start", "220"));
        var ready = await Wait(worker, "220", SteamImportState.ReadyToApply);
        Assert.False(ready.AutomaticRequested); Assert.False(File.Exists(Assert.Single(ready.Preview!).Destination));
    }

    [Theory]
    [InlineData(ImportErrorCode.CONFIGURATION_NOT_FOUND, SteamImportState.Unsupported)]
    [InlineData(ImportErrorCode.GAME_NOT_SUPPORTED, SteamImportState.Unsupported)]
    [InlineData(ImportErrorCode.PACKAGE_INVALID, SteamImportState.Failed)]
    [InlineData(ImportErrorCode.NETWORK_ERROR, SteamImportState.Failed)]
    public async Task FailuresReachTerminalStateAndNeverAutomaticallyApply(ImportErrorCode code, SteamImportState expected)
    {
        using var e = new TestEnvironment(); await using var worker = Coordinator(e, new FakeDownloads(e) { Failure = code });
        await worker.InitializeAsync();
        await worker.HandleAsync(new("settings", Confirmed: true, Settings: new(true)));
        await worker.HandleAsync(new("start", "2929250"));
        var task = await Wait(worker, "2929250", expected);
        Assert.Equal(code, task.ErrorCode); Assert.False(Directory.Exists(Path.Combine(e.Steam, "config")));
    }

    [Fact]
    public async Task ExplicitAutomaticConsentAppliesCurrentReadyTaskImmediatelyWithoutRestartAndLeavesOtherReadyTaskManual()
    {
        using var e = new TestEnvironment(); var downloads = new FakeDownloads(e);
        await using var worker = Coordinator(e, downloads); await worker.InitializeAsync();
        await worker.HandleAsync(new("start", "730"));
        var current = await Wait(worker, "730", SteamImportState.ReadyToApply);
        await worker.HandleAsync(new("start", "220"));
        var other = await Wait(worker, "220", SteamImportState.ReadyToApply);
        var enabled = worker.Settings with { AutomaticApply = true };
        Assert.False((await worker.HandleAsync(new("settings", "220", current.TaskId, Confirmed:true, Settings:enabled))).Ok);
        Assert.False(worker.Settings.AutomaticApply);
        Assert.Equal("CONSENT_REQUIRED", (await worker.HandleAsync(new("settings", "730", current.TaskId, Settings:enabled))).Error);
        var updated = await worker.HandleAsync(new("settings", "730", current.TaskId, Confirmed:true, Settings:enabled));
        Assert.True(updated.Ok); Assert.Equal(current.TaskId, updated.Task!.TaskId);
        var done = await Wait(worker, "730", SteamImportState.Completed);
        Assert.True(done.AutomaticRequested); Assert.Equal(2, downloads.Calls);
        Assert.True(File.Exists(Assert.Single(done.Preview!).Destination));
        Assert.Equal(SteamImportState.ReadyToApply, (await worker.HandleAsync(new("status", "220"))).Task!.State);
        Assert.False(File.Exists(Assert.Single(other.Preview!).Destination));
        await worker.HandleAsync(new("settings", Settings:worker.Settings with {AutomaticApply=false}));
        await worker.HandleAsync(new("start", "440"));
        var manual = await Wait(worker,"440",SteamImportState.ReadyToApply);
        Assert.False(manual.AutomaticRequested);
    }

    [Fact]
    public async Task EnablingAutomaticWithoutCurrentTaskDoesNotRetroactivelyApplyManualTask()
    {
        using var e = new TestEnvironment(); await using var worker=Coordinator(e,new FakeDownloads(e)); await worker.InitializeAsync();
        await worker.HandleAsync(new("start","730"));
        var ready=await Wait(worker,"730",SteamImportState.ReadyToApply);
        Assert.True((await worker.HandleAsync(new("settings",Confirmed:true,Settings:worker.Settings with {AutomaticApply=true}))).Ok);
        Assert.Equal(SteamImportState.ReadyToApply,(await worker.HandleAsync(new("status","730"))).Task!.State);
        Assert.False(File.Exists(Assert.Single(ready.Preview!).Destination));
    }

    [Fact]
    public async Task EnablingDuringDownloadAndDisablingBeforeVerificationLeavesFilesUnchanged()
    {
        using var e = new TestEnvironment(); var downloads=new FakeDownloads(e) { Pause=new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var worker=Coordinator(e,downloads);await worker.InitializeAsync();
        var started=await worker.HandleAsync(new("start","730"));
        await Wait(worker,"730",SteamImportState.Downloading);
        var enabled=await worker.HandleAsync(new("settings","730",started.Task!.TaskId,Confirmed:true,Settings:worker.Settings with {AutomaticApply=true}));
        Assert.True(enabled.Ok);Assert.True(enabled.Task!.AutomaticRequested);
        await worker.HandleAsync(new("settings",Settings:worker.Settings with {AutomaticApply=false}));
        downloads.Pause.SetResult();
        var ready=await Wait(worker,"730",SteamImportState.ReadyToApply);
        Assert.False(File.Exists(Assert.Single(ready.Preview!).Destination));
    }

    [Fact]
    public async Task CancellationAndReconnectionKeepAppIdAssociationAndDoNotRedownload()
    {
        using var e = new TestEnvironment(); var downloads = new FakeDownloads(e) { WaitForCancellation = true };
        await using var worker = Coordinator(e, downloads); await worker.InitializeAsync();
        var first = await worker.HandleAsync(new("start", "730"));
        var later = await worker.HandleAsync(new("status", "730"));
        Assert.Equal(first.Task!.TaskId, later.Task!.TaskId);
        Assert.False((await worker.HandleAsync(new("cancel", "220", first.Task.TaskId))).Ok);
        Assert.True((await worker.HandleAsync(new("cancel", "730", first.Task.TaskId))).Ok);
        await Wait(worker, "730", SteamImportState.Cancelled);
        Assert.False(Directory.Exists(Path.Combine(e.Steam, "config")));
    }

    [Fact]
    public async Task SettingsAndCompletedTasksPersistAndInterruptedReadyTasksRequireRetry()
    {
        using var e = new TestEnvironment(); Guid id;
        await using (var worker = Coordinator(e, new FakeDownloads(e)))
        {
            await worker.InitializeAsync();
            await worker.HandleAsync(new("settings", Settings: new(false, "custom", "#12345678")));
            await worker.HandleAsync(new("start", "730"));
            id = (await Wait(worker, "730", SteamImportState.ReadyToApply)).TaskId;
        }
        await using var restored = Coordinator(e, new FakeDownloads(e)); await restored.InitializeAsync();
        Assert.Equal("#12345678", restored.Settings.CustomColor);
        Assert.False(restored.Settings.AutomaticApply);
        var state = (await restored.HandleAsync(new("status", "730"))).Task!;
        Assert.Equal(id, state.TaskId); Assert.Equal(SteamImportState.Failed, state.State);
        Assert.Contains("restarted", state.Message);
        await restored.HandleAsync(new("settings", Settings: new()));
        Assert.Equal("native", restored.Settings.ColorMode); Assert.False(restored.Settings.AutomaticApply);
        Assert.False((await restored.HandleAsync(new("settings", Settings: new(false, "custom", "url(evil)")))).Ok);
    }

    [Theory]
    [InlineData("automatic", "native")]
    [InlineData("custom", "custom")]
    public async Task LegacyColorMigrationPreservesConsentColorAndUnrelatedSettings(string previous, string expected)
    {
        using var e = new TestEnvironment();
        var root = Path.Combine(e.Paths.DataRoot, "steam-bridge"); Directory.CreateDirectory(root);
        var saved = new SteamBridgeSettings(true, previous, "#123456");
        await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), JsonSerializer.Serialize(saved, BackgroundImportCoordinator.Json));
        var unrelated = Path.Combine(e.Paths.DataRoot, "unrelated.json"); await File.WriteAllTextAsync(unrelated, "preserve");
        await using var worker = Coordinator(e, new FakeDownloads(e)); await worker.InitializeAsync();
        Assert.Equal(expected, worker.Settings.ColorMode); Assert.True(worker.Settings.AutomaticApply);
        Assert.Equal("#123456", worker.Settings.CustomColor); Assert.Equal("preserve", await File.ReadAllTextAsync(unrelated));
        if (previous == "automatic")
            Assert.Equal(expected, JsonSerializer.Deserialize<SteamBridgeSettings>(await File.ReadAllTextAsync(Path.Combine(root, "settings.json")), BackgroundImportCoordinator.Json)!.ColorMode);
        var compatible = await worker.HandleAsync(new("settings", Settings: saved));
        Assert.True(compatible.Ok); Assert.Equal(expected, compatible.Settings!.ColorMode);
    }

    internal sealed class FakeDownloads(TestEnvironment e) : IWebsiteImportService
    {
        public int Calls;
        public ImportErrorCode? Failure;
        public bool WaitForCancellation;
        public TaskCompletionSource? Pause;
        public async Task<WebsiteImportResult> DownloadAsync(WebsiteImportRequest request, CancellationToken token = default, IProgress<WebsiteImportProgress>? progress = null)
        {
            Interlocked.Increment(ref Calls);
            progress?.Report(new("Downloading", 10, 20));
            if (Pause is not null) await Pause.Task.WaitAsync(token);
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, token);
            token.ThrowIfCancellationRequested();
            if (Failure is { } error) return WebsiteImportResult.Failure("Mock failure", error);
            var relative = $"temp/import-{Guid.NewGuid():N}/{request.AppId}.lua";
            var file = e.CreateFile(relative, $"addappid({request.AppId})");
            return WebsiteImportResult.Success([file], Path.GetDirectoryName(file)!, "HTTPS and SHA-256 verified");
        }
    }
}

[CollectionDefinition("Steam pipe", DisableParallelization = true)]
public sealed class SteamPipeCollection;

[Collection("Steam pipe")]
public sealed class SteamPipeTests
{
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task RealRestrictedPipeSupportsReconnectRejectsMalformedInputAndShutsDown()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var e = new TestEnvironment();
        var settings = new TestSettingsService(new AppSettings { SteamPath = e.Steam });
        var worker = new BackgroundImportCoordinator(new BackgroundImportTests.FakeDownloads(e),
            new InjectionService(e.Paths, settings, new SteamService(), new HistoryRepository(e.Paths, NullLogger<HistoryRepository>.Instance),
                new SafeZipExtractor(), TimeProvider.System, NullLogger<InjectionService>.Instance), settings, e.Paths, new SteamService());
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var pipeName = SteamBridgePipeServer.PipeName + ".test." + Guid.NewGuid().ToString("N");
        var server = Task.Run(() => new SteamBridgePipeServer(worker).RunAsync(lifetime.Token, pipeName));
        async Task<JsonDocument> Send(string request)
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(3000, lifetime.Token); client.ReadMode = PipeTransmissionMode.Message;
            await client.WriteAsync(Encoding.UTF8.GetBytes(request), lifetime.Token);
            using var result = new MemoryStream(); var buffer = new byte[4096];
            do { var read = await client.ReadAsync(buffer, lifetime.Token); if (read == 0) break; result.Write(buffer, 0, read); } while (!client.IsMessageComplete);
            return JsonDocument.Parse(result.ToArray());
        }
        try
        {
            using var malformed = await Send("{bad"); Assert.False(malformed.RootElement.GetProperty("ok").GetBoolean());
            using var first = await Send("{\"op\":\"start\",\"appId\":\"730\"}"); Assert.True(first.RootElement.GetProperty("ok").GetBoolean());
            using var reconnected = await Send("{\"op\":\"status\",\"appId\":\"730\"}");
            Assert.Equal(first.RootElement.GetProperty("task").GetProperty("taskId").GetString(), reconnected.RootElement.GetProperty("task").GetProperty("taskId").GetString());
            using var unexpected = await Send("{\"op\":\"start\",\"appId\":\"730\",\"origin\":\"https://evil\"}"); Assert.False(unexpected.RootElement.GetProperty("ok").GetBoolean());
            using var stopped = await Send("{\"op\":\"shutdown\"}"); Assert.True(stopped.RootElement.GetProperty("ok").GetBoolean());
            await server.WaitAsync(lifetime.Token);
        }
        finally { lifetime.Cancel(); await server; }
    }
}
