using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Results;
using NightInjection.Core.Validation;

namespace NightInjection.Infrastructure.SteamBridge;

public sealed class BackgroundImportCoordinator(IWebsiteImportService downloads, IInjectionService injection,
    ISettingsService appSettings, IAppPathService paths, ISteamService steam) : IAsyncDisposable
{
    private sealed class TaskData(SteamImportSnapshot snapshot)
    {
        public SteamImportSnapshot Snapshot = snapshot;
        public readonly CancellationTokenSource Cancellation = new(TimeSpan.FromMinutes(3));
        public InjectionPlan? Plan;
        public string? TemporaryDirectory;
        public Task Work = Task.CompletedTask;
    }
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly object _sync = new();
    private readonly SemaphoreSlim _persistence = new(1, 1);
    private readonly Dictionary<Guid, TaskData> _tasks = [];
    private readonly Dictionary<string, Guid> _latest = [];
    private SteamBridgeSettings _settings = new();
    private bool _disposed;
    private string Root => Path.Combine(paths.DataRoot, "steam-bridge");
    public SteamBridgeSettings Settings { get { lock (_sync) return _settings; } }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        await appSettings.InitializeAsync(token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(appSettings.Current.SteamPath))
        {
            var detected = await steam.DetectAsync(null, token).ConfigureAwait(false);
            if (detected.IsValid) await appSettings.SaveAsync(appSettings.Current with { SteamPath = detected.Path }, token).ConfigureAwait(false);
        }
        Directory.CreateDirectory(Root);
        var settings = await ReadAsync<SteamBridgeSettings>("settings.json", token).ConfigureAwait(false);
        if (settings is not null && ValidSettings(settings)) _settings = NormalizeSettings(settings);
        var snapshots = await ReadAsync<List<SteamImportSnapshot>>("tasks.json", token).ConfigureAwait(false);
        foreach (var saved in snapshots?.TakeLast(32) ?? [])
        {
            if (!WebsiteImportActivation.IsCanonicalAppId(saved.AppId) || saved.TaskId == Guid.Empty) continue;
            var restored = IsTerminal(saved.State) ? saved : saved with { State = SteamImportState.Failed,
                ErrorCode = ImportErrorCode.SERVER_ERROR, Message = "Background worker restarted. Retry to prepare a new verified package." };
            _tasks[restored.TaskId] = new TaskData(restored);
            _latest[restored.AppId] = restored.TaskId;
        }
        if (settings?.ColorMode == "automatic") await PersistAsync(token).ConfigureAwait(false);
    }

    public async Task<SteamBridgeReply> HandleAsync(SteamBridgeRequest request, CancellationToken token = default)
    {
        if (_disposed) return new(false, Error: "WORKER_UNAVAILABLE", Message: "Background worker is shutting down.");
        switch (request.Op)
        {
            case "settings":
                if (request.Settings is null) return new(true, Settings: Settings);
                if (!ValidSettings(request.Settings)) return new(false, Error: "VALIDATION_FAILED", Message: "Invalid settings or color.");
                TaskData? consentTask = null;
                lock (_sync)
                {
                    if (request.AppId is not null)
                    {
                        if (!WebsiteImportActivation.IsCanonicalAppId(request.AppId)) return Invalid();
                        if (request.TaskId is Guid selected)
                        {
                            if (!_tasks.TryGetValue(selected, out consentTask) || consentTask.Snapshot.AppId != request.AppId) return Invalid();
                        }
                        else if (_latest.TryGetValue(request.AppId, out var selectedLatest)) consentTask = _tasks[selectedLatest];
                    }
                    else if (request.TaskId is not null) return Invalid();
                    if (!_settings.AutomaticApply && request.Settings.AutomaticApply && !request.Confirmed)
                        return new(false, Settings: _settings, Error: "CONSENT_REQUIRED", Message: "Confirm that verified files may be written or replaced after an import click, without another confirmation.");
                    _settings = NormalizeSettings(request.Settings);
                    if (!_settings.AutomaticApply)
                        foreach (var pending in _tasks.Values.Where(t => t.Snapshot.AutomaticRequested && t.Snapshot.State == SteamImportState.ApplyingChanges)) pending.Cancellation.Cancel();
                }
                await PersistAsync(token).ConfigureAwait(false);
                lock (_sync)
                {
                    // The informed Enable control may explicitly include the current import.
                    // Other manual tasks retain their original approval requirement.
                    if (request.Confirmed && request.Settings.AutomaticApply && _settings.AutomaticApply && consentTask is not null
                        && !consentTask.Cancellation.IsCancellationRequested && !IsTerminal(consentTask.Snapshot.State)
                        && consentTask.Snapshot.State != SteamImportState.ApplyingChanges)
                    {
                        consentTask.Snapshot = consentTask.Snapshot with { AutomaticRequested = true, Revision = consentTask.Snapshot.Revision + 1 };
                        if (consentTask.Snapshot.State == SteamImportState.ReadyToApply && consentTask.Plan?.IsValid == true)
                        {
                            Update(consentTask, SteamImportState.ApplyingChanges, "Automatic Apply: applying the current verified package");
                            consentTask.Work = Task.Run(() => ApplyAsync(consentTask));
                        }
                    }
                    return new(true, consentTask?.Snapshot, _settings);
                }
            case "start":
                if (!WebsiteImportActivation.IsCanonicalAppId(request.AppId) || request.GameName?.Length > 256)
                    return Invalid();
                TaskData data;
                lock (_sync)
                {
                    if (_latest.TryGetValue(request.AppId!, out var id) && _tasks.TryGetValue(id, out var existing)
                        && (!request.Retry || !IsTerminal(existing.Snapshot.State))) return new(true, existing.Snapshot, _settings);
                    if (_tasks.Values.Count(t => !IsTerminal(t.Snapshot.State)) >= 4)
                        return new(false, Error: "RATE_LIMITED", Message: "Finish or cancel an existing import first.");
                    data = new(new(Guid.NewGuid(), request.AppId!, request.GameName ?? $"Steam AppID {request.AppId}",
                        SteamImportState.Connecting, "Connecting to DarkDevil", AutomaticRequested: _settings.AutomaticApply));
                    _tasks[data.Snapshot.TaskId] = data; _latest[request.AppId!] = data.Snapshot.TaskId;
                    Prune();
                    data.Work = Task.Run(() => PrepareAsync(data));
                }
                return new(true, Snapshot(data), Settings);
            case "status":
                if (!WebsiteImportActivation.IsCanonicalAppId(request.AppId)) return Invalid();
                lock (_sync)
                {
                    if (request.TaskId is Guid taskId && _tasks.TryGetValue(taskId, out var task))
                        return task.Snapshot.AppId == request.AppId ? new(true, task.Snapshot, _settings) : Invalid();
                    if (request.AppId is not null && _latest.TryGetValue(request.AppId, out var latest))
                        return new(true, _tasks[latest].Snapshot, _settings);
                    return new(true, Settings: _settings);
                }
            case "preview":
                lock (_sync)
                {
                    if (request.TaskId is not Guid previewId || !_tasks.TryGetValue(previewId, out var previewTask)
                        || previewTask.Snapshot.AppId != request.AppId || previewTask.Plan is null
                        || request.PreviewOffset < 0 || request.PreviewOffset >= previewTask.Plan.Entries.Count) return Invalid();
                    var page = previewTask.Plan.Entries.Skip(request.PreviewOffset).Take(40)
                        .Select(e => new SteamImportPreview(Path.GetFileName(e.SourcePath), e.DestinationPath, e.Size, e.WillOverwrite)).ToArray();
                    return new(true, previewTask.Snapshot with { Preview = page, PreviewOffset = request.PreviewOffset }, _settings);
                }
            case "cancel":
            case "apply":
                lock (_sync)
                {
                    if (request.TaskId is not Guid taskId || !_tasks.TryGetValue(taskId, out var target)
                        || target.Snapshot.AppId != request.AppId) return Invalid();
                    if (request.Op == "cancel")
                    {
                        target.Cancellation.Cancel();
                        if (target.Snapshot.State == SteamImportState.ReadyToApply)
                        {
                            Update(target, SteamImportState.Cancelled, "Import cancelled. No changes were applied.", ImportErrorCode.CANCELLED);
                            target.Work = CleanupAsync(target);
                        }
                    }
                    else
                    {
                        if (!request.Confirmed) return new(false, target.Snapshot, _settings, "CONSENT_REQUIRED", "Review and confirm the file changes before applying.");
                        if (target.Snapshot.State != SteamImportState.ReadyToApply || target.Plan?.IsValid != true) return Invalid();
                        Update(target, SteamImportState.ApplyingChanges, "Applying verified changes");
                        target.Work = Task.Run(() => ApplyAsync(target));
                    }
                    return new(true, target.Snapshot, _settings);
                }
            default: return Invalid();
        }
    }

    public static bool IsTerminal(SteamImportState state) => state is SteamImportState.Completed or SteamImportState.Failed
        or SteamImportState.Cancelled or SteamImportState.Unsupported;
    private static SteamBridgeReply Invalid() => new(false, Error: "VALIDATION_FAILED", Message: "Invalid operation, AppID or task association.");
    private static bool ValidSettings(SteamBridgeSettings settings) => settings.Version == 2
        && settings.ColorMode is "native" or "automatic" or "custom"
        && Regex.IsMatch(settings.CustomColor ?? "", "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");
    private static SteamBridgeSettings NormalizeSettings(SteamBridgeSettings settings) => settings with {
        ColorMode = settings.ColorMode == "automatic" ? "native" : settings.ColorMode,
        CustomColor = settings.CustomColor.ToUpperInvariant()
    };
    private SteamImportSnapshot Snapshot(TaskData data) { lock (_sync) return data.Snapshot; }
    private void Update(TaskData data, SteamImportState state, string message, ImportErrorCode? error = null)
    {
        lock (_sync) data.Snapshot = data.Snapshot with { State = state, Message = message, ErrorCode = error, Revision = data.Snapshot.Revision + 1 };
    }
    private async Task PrepareAsync(TaskData data)
    {
        try
        {
            await appSettings.InitializeAsync(data.Cancellation.Token).ConfigureAwait(false);
            await PersistAsync().ConfigureAwait(false);
            var origin = WebsiteImportActivation.DefaultOrigin;
#if DEBUG
            var developmentOrigin = Environment.GetEnvironmentVariable("NIGHT_INJECTION_STEAM_TEST_ORIGIN");
            if (WebsiteImportActivation.DevelopmentImportsEnabled && Uri.TryCreate(developmentOrigin, UriKind.Absolute, out var local)
                && WebsiteImportActivation.IsTrustedOrigin(local, true)) origin = local;
#endif
            var progress = new InlineProgress(value =>
            {
                lock (_sync)
                {
                    if (data.Cancellation.IsCancellationRequested || IsTerminal(data.Snapshot.State)) return;
                    var state = value.Stage switch
                    {
                        "Checking configuration" => SteamImportState.CheckingConfiguration,
                        "Preparing package" => SteamImportState.PreparingPackage,
                        "Downloading" => SteamImportState.Downloading,
                        "Verifying package" => SteamImportState.VerifyingPackage,
                        "Ready for review" => SteamImportState.VerifyingPackage,
                        _ => SteamImportState.Connecting,
                    };
                    data.Snapshot = data.Snapshot with { State = state, Message = value.Stage,
                        BytesReceived = value.BytesReceived > 0 ? value.BytesReceived : data.Snapshot.BytesReceived,
                        TotalBytes = value.TotalBytes ?? data.Snapshot.TotalBytes, Revision = data.Snapshot.Revision + 1 };
                }
            });
            var result = await downloads.DownloadAsync(new(data.Snapshot.AppId, origin), data.Cancellation.Token, progress).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var error = result.ErrorCode ?? ImportErrorCode.DOWNLOAD_FAILED;
                Update(data, error is ImportErrorCode.CONFIGURATION_NOT_FOUND or ImportErrorCode.GAME_NOT_SUPPORTED
                    ? SteamImportState.Unsupported : SteamImportState.Failed, result.Message, error);
                return;
            }
            data.TemporaryDirectory = result.TemporaryDirectory;
            data.Plan = await injection.BuildFilePlanAsync(result.FilePaths, appSettings.Current.SteamPath, data.Cancellation.Token).ConfigureAwait(false);
            if (!data.Plan.IsValid)
            {
                Update(data, SteamImportState.Failed, string.Join("; ", data.Plan.Errors), ImportErrorCode.VALIDATION_FAILED);
                return;
            }
            bool automatic;
            lock (_sync)
            {
                automatic = data.Snapshot.AutomaticRequested && _settings.AutomaticApply;
                data.Snapshot = data.Snapshot with {
                    State = automatic ? SteamImportState.ApplyingChanges : SteamImportState.ReadyToApply,
                    Message = automatic ? "Automatic Apply: applying verified changes" : "Verified package ready. Review the changes before applying.",
                    Verification = result.Message.Contains("Ed25519 source signature verified", StringComparison.Ordinal)
                        ? "Ed25519 signature and SHA-256 verified" : "HTTPS and SHA-256 verified; signing pin is not configured",
                    Preview = data.Plan.Entries.Take(40).Select(e => new SteamImportPreview(Path.GetFileName(e.SourcePath), e.DestinationPath, e.Size, e.WillOverwrite)).ToArray(),
                    FileCount = data.Plan.Entries.Count, OverwriteCount = data.Plan.OverwriteCount, Revision = data.Snapshot.Revision + 1 };
            }
            if (automatic)
            {
                await ApplyAsync(data).ConfigureAwait(false);
            }
            else data.Cancellation.CancelAfter(Timeout.InfiniteTimeSpan);
        }
        catch (OperationCanceledException) { Update(data, SteamImportState.Cancelled, "Import cancelled. Pending writes were rolled back.", ImportErrorCode.CANCELLED); }
        catch (Exception exception) { Update(data, SteamImportState.Failed, $"Import could not continue: {exception.Message}", ImportErrorCode.SERVER_ERROR); }
        finally
        {
            if (IsTerminal(Snapshot(data).State)) await CleanupAsync(data).ConfigureAwait(false);
            await PersistAsync().ConfigureAwait(false);
        }
    }
    private async Task ApplyAsync(TaskData data)
    {
        try
        {
            var result = await injection.ExecutePlanAsync(data.Plan!, data.Cancellation.Token).ConfigureAwait(false);
            Update(data, result.Succeeded ? SteamImportState.Completed : data.Cancellation.IsCancellationRequested
                ? SteamImportState.Cancelled : SteamImportState.Failed,
                result.Succeeded ? "Configuration applied successfully" : result.Message,
                result.Succeeded ? null : data.Cancellation.IsCancellationRequested ? ImportErrorCode.CANCELLED : ImportErrorCode.APPLY_FAILED);
        }
        catch (OperationCanceledException) { Update(data, SteamImportState.Cancelled, "Apply cancelled. Pending writes were rolled back.", ImportErrorCode.CANCELLED); }
        catch (Exception exception) { Update(data, SteamImportState.Failed, $"Apply failed: {exception.Message}", ImportErrorCode.APPLY_FAILED); }
        finally { await CleanupAsync(data).ConfigureAwait(false); await PersistAsync().ConfigureAwait(false); }
    }
    private async Task CleanupAsync(TaskData data)
    {
        if (data.Plan is not null) await injection.CleanupPlanAsync(data.Plan).ConfigureAwait(false);
        if (data.TemporaryDirectory is string directory)
        {
            var full = Path.GetFullPath(directory);
            if (full.StartsWith(Path.GetFullPath(paths.TemporaryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                try { if (Directory.Exists(full)) Directory.Delete(full, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            data.TemporaryDirectory = null;
        }
    }
    private void Prune()
    {
        foreach (var item in _tasks.Where(t => IsTerminal(t.Value.Snapshot.State) && t.Value.Work.IsCompleted).Take(Math.Max(0, _tasks.Count - 32)).ToArray())
        {
            _tasks.Remove(item.Key); item.Value.Cancellation.Dispose();
            if (_latest.GetValueOrDefault(item.Value.Snapshot.AppId) == item.Key) _latest.Remove(item.Value.Snapshot.AppId);
        }
    }
    private async Task<T?> ReadAsync<T>(string name, CancellationToken token)
    {
        var file = Path.Combine(Root, name);
        if (!File.Exists(file) || new FileInfo(file).Length > 1024 * 1024) return default;
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(file, token).ConfigureAwait(false), Json); }
        catch (Exception e) when (e is IOException or JsonException) { return default; }
    }
    private async Task PersistAsync(CancellationToken token = default)
    {
        await _persistence.WaitAsync(token).ConfigureAwait(false);
        try
        {
            SteamBridgeSettings settings; SteamImportSnapshot[] tasks;
            lock (_sync) { settings = _settings; tasks = _tasks.Values.Select(t => t.Snapshot).ToArray(); }
            Directory.CreateDirectory(Root);
            foreach (var (name, content) in new[] { ("settings.json", JsonSerializer.Serialize(settings, Json)), ("tasks.json", JsonSerializer.Serialize(tasks, Json)) })
            {
                var file = Path.Combine(Root, name); var staged = file + ".tmp";
                await File.WriteAllTextAsync(staged, content, token).ConfigureAwait(false);
                File.Move(staged, file, true);
            }
        }
        finally { _persistence.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        TaskData[] tasks; lock (_sync) tasks = _tasks.Values.ToArray();
        foreach (var task in tasks) task.Cancellation.Cancel();
        await Task.WhenAll(tasks.Select(t => t.Work)).ConfigureAwait(false);
        foreach (var task in tasks) { await CleanupAsync(task).ConfigureAwait(false); task.Cancellation.Dispose(); }
    }
    private sealed class InlineProgress(Action<WebsiteImportProgress> report) : IProgress<WebsiteImportProgress>
    { public void Report(WebsiteImportProgress value) => report(value); }
}
