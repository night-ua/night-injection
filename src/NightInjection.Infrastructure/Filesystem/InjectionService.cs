using System.Text.Json;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Results;
using NightInjection.Core.Validation;

namespace NightInjection.Infrastructure.Filesystem;

public sealed partial class InjectionService(
    IAppPathService paths,
    ISteamService steamService,
    IHistoryRepository history,
    SafeZipExtractor zipExtractor,
    TimeProvider timeProvider,
    ILogger<InjectionService> logger) : IInjectionService
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<InjectionPlan> BuildFilePlanAsync(
        IReadOnlyCollection<string> files,
        string steamPath,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var entries = new List<InjectionPlanEntry>();
        var destinationSources = new Dictionary<string, string>(PathComparer);
        var verification = await steamService.VerifyAsync(steamPath, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            errors.Add(verification.Message);
            return CreatePlan(steamPath, entries, warnings, errors);
        }

        if (files.Count == 0)
        {
            errors.Add("No files were selected.");
            return CreatePlan(verification.Path, entries, warnings, errors);
        }

        var planId = Guid.NewGuid();
        var temporaryRoot = Path.Combine(paths.TemporaryRoot, $"plan-{planId:N}");
        var hasTemporaryFiles = false;
        var steamDirectories = steamService.GetDirectories(verification.Path);

        foreach (var sourceValue in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source;
            try
            {
                source = Path.GetFullPath(sourceValue);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                errors.Add($"{sourceValue}: invalid path.");
                continue;
            }

            if (!File.Exists(source))
            {
                errors.Add($"{Path.GetFileName(source)}: file was not found.");
                continue;
            }

            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension == ".zip")
            {
                var extractDirectory = Path.Combine(temporaryRoot, $"archive-{entries.Count}");
                try
                {
                    var extracted = await zipExtractor.ExtractAsync(
                        source,
                        extractDirectory,
                        cancellationToken).ConfigureAwait(false);
                    hasTemporaryFiles = true;
                    var supported = extracted.Where(IsSupportedFile).ToArray();
                    if (supported.Length == 0)
                    {
                        errors.Add($"ZIP archive {Path.GetFileName(source)} contains no Lua or manifest files.");
                        continue;
                    }

                    foreach (var extractedPath in supported)
                    {
                        AddEntry(
                            extractedPath,
                            $"{Path.GetFileName(source)} > {Path.GetRelativePath(extractDirectory, extractedPath)}",
                            steamDirectories,
                            entries,
                            destinationSources,
                            errors);
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(source)}: {exception.Message}");
                }

                continue;
            }

            if (!IsSupportedFile(source))
            {
                warnings.Add($"{Path.GetFileName(source)} was ignored because its type is unsupported.");
                continue;
            }

            AddEntry(
                source,
                Path.GetFileName(source),
                steamDirectories,
                entries,
                destinationSources,
                errors);
        }

        return new InjectionPlan
        {
            Id = planId,
            SourceKind = InjectionSourceKind.LocalFiles,
            SteamPath = verification.Path,
            TemporaryDirectory = hasTemporaryFiles ? temporaryRoot : null,
            Entries = entries,
            Warnings = warnings,
            Errors = errors
        };
    }

    public async Task<OperationResult> ExecutePlanAsync(
        InjectionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsValid)
        {
            return OperationResult.Failure(
                plan.Errors.FirstOrDefault() ?? "The injection plan is not valid.",
                warnings: plan.Warnings);
        }

        var verification = await steamService.VerifyAsync(plan.SteamPath, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            return OperationResult.Failure(verification.Message);
        }

        var allowed = steamService.GetDirectories(verification.Path);
        if (plan.Entries.Any(entry => !IsAllowedDestination(entry.DestinationPath, allowed)))
        {
            return OperationResult.Failure("The plan contains a destination outside the allowed Steam folders.");
        }

        var transactionId = Guid.NewGuid().ToString("N");
        var staged = new List<StagedFile>(plan.Entries.Count);
        var committed = new List<StagedFile>(plan.Entries.Count);
        var affected = new List<string>(plan.Entries.Count);

        try
        {
            foreach (var entry in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(entry.SourcePath))
                {
                    throw new FileNotFoundException("A planned source file is no longer available.", entry.SourcePath);
                }

                var destinationDirectory = Path.GetDirectoryName(entry.DestinationPath)!;
                Directory.CreateDirectory(destinationDirectory);
                var temporary = Path.Combine(
                    destinationDirectory,
                    $".{Path.GetFileName(entry.DestinationPath)}.{transactionId}.tmp");
                var backup = Path.Combine(
                    destinationDirectory,
                    $".{Path.GetFileName(entry.DestinationPath)}.{transactionId}.bak");
                await CopyAsync(entry.SourcePath, temporary, cancellationToken).ConfigureAwait(false);
                staged.Add(new StagedFile(entry.DestinationPath, temporary, backup, File.Exists(entry.DestinationPath)));
            }

            foreach (var file in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Existed)
                {
                    File.Copy(file.Destination, file.Backup, overwrite: false);
                }

                File.Move(file.Temporary, file.Destination, overwrite: true);
                committed.Add(file);
                affected.Add(file.Destination);
            }

            await history.AddAsync(
                new NewHistoryRecord(
                    timeProvider.GetUtcNow(),
                    plan.SourceKind == InjectionSourceKind.AppIdBundle ? "AppID" : "Inject",
                    plan.AppId,
                    string.Join(", ", plan.Entries.Select(static entry => entry.SourceDisplayName).Distinct()),
                    "Success",
                    JsonSerializer.Serialize(new
                    {
                        plan.Id,
                        destinations = affected,
                        overwrites = plan.OverwriteCount
                    }),
                    plan.Repository),
                cancellationToken).ConfigureAwait(false);

            LogInjectionCompleted(logger, affected.Count, plan.AppId);
            return OperationResult.Success(
                $"Injection completed: {affected.Count} destination(s) updated.",
                affected,
                plan.Warnings);
        }
        catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException
                                             or OperationCanceledException)
        {
            RollBack(committed);
            LogInjectionFailed(logger, exception, plan.AppId);
            if (exception is not OperationCanceledException)
            {
                await TryRecordFailureAsync(plan, exception.Message).ConfigureAwait(false);
            }

            return OperationResult.Failure(
                exception is OperationCanceledException
                    ? "The operation was cancelled."
                    : $"Injection failed: {exception.Message}",
                affected,
                plan.Warnings);
        }
        finally
        {
            foreach (var file in staged)
            {
                TryDelete(file.Temporary);
                TryDelete(file.Backup);
            }

            await CleanupPlanAsync(plan).ConfigureAwait(false);
        }
    }

    public async Task<OperationResult> RemoveAppIdAsync(
        string steamPath,
        string appId,
        CancellationToken cancellationToken = default)
    {
        string normalizedId;
        try
        {
            normalizedId = AppIdValidator.Normalize(appId);
        }
        catch (ArgumentException exception)
        {
            return OperationResult.Failure(exception.Message);
        }

        var verification = await steamService.VerifyAsync(steamPath, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            return OperationResult.Failure(verification.Message);
        }

        var directories = steamService.GetDirectories(verification.Path);
        var targets = new[]
        {
            Path.Combine(directories.Plugin, $"{normalizedId}.lua"),
            Path.Combine(directories.Lua, $"{normalizedId}.lua"),
            Path.Combine(directories.DepotCache, $"{normalizedId}.manifest")
        };
        var removed = new List<string>();
        try
        {
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(target))
                {
                    File.Delete(target);
                    removed.Add(target);
                }
            }

            await history.AddAsync(new NewHistoryRecord(
                timeProvider.GetUtcNow(),
                "Remove",
                normalizedId,
                null,
                "Success",
                removed.Count == 0 ? "No matching files existed." : JsonSerializer.Serialize(removed),
                null), cancellationToken).ConfigureAwait(false);
            return OperationResult.Success($"Removed {removed.Count} file(s).", removed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure($"Could not remove AppID files: {exception.Message}", removed);
        }
    }

    public async Task<OperationResult> ClearPluginsAsync(
        string steamPath,
        CancellationToken cancellationToken = default)
    {
        var verification = await steamService.VerifyAsync(steamPath, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            return OperationResult.Failure(verification.Message);
        }

        var pluginDirectory = steamService.GetDirectories(verification.Path).Plugin;
        var removed = new List<string>();
        try
        {
            if (Directory.Exists(pluginDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(pluginDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(file);
                    removed.Add(file);
                }
            }

            await history.AddAsync(new NewHistoryRecord(
                timeProvider.GetUtcNow(),
                "ClearPlugins",
                null,
                null,
                "Success",
                JsonSerializer.Serialize(removed),
                null), cancellationToken).ConfigureAwait(false);
            return OperationResult.Success($"Cleared {removed.Count} plug-in file(s).", removed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Failure($"Could not clear the plug-in folder: {exception.Message}", removed);
        }
    }

    public Task CleanupPlanAsync(InjectionPlan plan)
    {
        if (string.IsNullOrWhiteSpace(plan.TemporaryDirectory))
        {
            return Task.CompletedTask;
        }

        try
        {
            var root = Path.GetFullPath(paths.TemporaryRoot);
            var candidate = Path.GetFullPath(plan.TemporaryDirectory);
            var prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(candidate))
            {
                Directory.Delete(candidate, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogCleanupFailed(logger, exception, plan.TemporaryDirectory);
        }

        return Task.CompletedTask;
    }

    private static InjectionPlan CreatePlan(
        string steamPath,
        IReadOnlyList<InjectionPlanEntry> entries,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors) =>
        new()
        {
            SourceKind = InjectionSourceKind.LocalFiles,
            SteamPath = steamPath,
            Entries = entries,
            Warnings = warnings,
            Errors = errors
        };

    private static void AddEntry(
        string source,
        string display,
        SteamDirectories directories,
        ICollection<InjectionPlanEntry> entries,
        IDictionary<string, string> destinationSources,
        ICollection<string> errors)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var fileType = extension == ".lua" ? InjectionFileType.Lua : InjectionFileType.Manifest;
        var destinations = fileType == InjectionFileType.Lua
            ? new[]
            {
                Path.Combine(directories.Plugin, Path.GetFileName(source)),
                Path.Combine(directories.Lua, Path.GetFileName(source))
            }
            : new[] { Path.Combine(directories.DepotCache, Path.GetFileName(source)) };

        foreach (var destination in destinations)
        {
            if (destinationSources.TryGetValue(destination, out var previous))
            {
                errors.Add($"Destination collision: {display} and {previous} both target {destination}.");
                continue;
            }

            destinationSources.Add(destination, display);
            entries.Add(new InjectionPlanEntry(
                source,
                display,
                destination,
                fileType,
                new FileInfo(source).Length,
                File.Exists(destination)));
        }
    }

    private static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".lua", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedDestination(string destination, SteamDirectories directories) =>
        IsUnder(destination, directories.Plugin)
        || IsUnder(destination, directories.Lua)
        || IsUnder(destination, directories.DepotCache);

    private static bool IsUnder(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullRoot = Path.GetFullPath(root);
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81_920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void RollBack(IEnumerable<StagedFile> committed)
    {
        foreach (var file in committed.Reverse())
        {
            try
            {
                if (file.Existed && File.Exists(file.Backup))
                {
                    File.Copy(file.Backup, file.Destination, overwrite: true);
                }
                else
                {
                    File.Delete(file.Destination);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task TryRecordFailureAsync(InjectionPlan plan, string details)
    {
        try
        {
            await history.AddAsync(new NewHistoryRecord(
                timeProvider.GetUtcNow(),
                plan.SourceKind == InjectionSourceKind.AppIdBundle ? "AppID" : "Inject",
                plan.AppId,
                null,
                "Error",
                details,
                plan.Repository)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            LogHistoryFailed(logger, exception);
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

    private sealed record StagedFile(string Destination, string Temporary, string Backup, bool Existed);

    [LoggerMessage(LogLevel.Information, "Injection completed with {Count} destinations for AppID {AppId}")]
    private static partial void LogInjectionCompleted(ILogger logger, int count, string? appId);

    [LoggerMessage(LogLevel.Error, "Injection failed for AppID {AppId}")]
    private static partial void LogInjectionFailed(ILogger logger, Exception exception, string? appId);

    [LoggerMessage(LogLevel.Debug, "Could not remove plan directory {Path}")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(LogLevel.Warning, "Could not record failed operation in history")]
    private static partial void LogHistoryFailed(ILogger logger, Exception exception);
}
