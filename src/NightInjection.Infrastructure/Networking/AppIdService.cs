using System.Net;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;
using NightInjection.Core.Services;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Filesystem;

namespace NightInjection.Infrastructure.Networking;

public sealed partial class AppIdService(
    HttpClient httpClient,
    IAppPathService paths,
    ISteamService steamService,
    SafeZipExtractor zipExtractor,
    ILogger<AppIdService> logger) : IAppIdService
{
    private const long MaximumDownloadBytes = 100L * 1024 * 1024;
    private static readonly string[] Repositories =
    [
        "https://github.com/LightnigFast/ProjectLightningManifests",
        "https://github.com/SPIN0ZAi/SB_manifest_DB",
        "https://github.com/dvahana2424-web/sojogamesdatabase1",
        "https://github.com/sojorepo/sojogames",
        "https://github.com/SteamAutoCracks/ManifestHub"
    ];

    public async Task<InjectionPlan> BuildPlanAsync(
        string steamPath,
        string appId,
        bool force,
        CancellationToken cancellationToken = default)
    {
        string normalizedId;
        try
        {
            normalizedId = AppIdValidator.Normalize(appId);
        }
        catch (ArgumentException exception)
        {
            return ErrorPlan(steamPath, appId, exception.Message);
        }

        var verification = await steamService.VerifyAsync(steamPath, cancellationToken).ConfigureAwait(false);
        if (!verification.IsValid)
        {
            return ErrorPlan(steamPath, normalizedId, verification.Message);
        }

        var steamDirectories = steamService.GetDirectories(verification.Path);
        var duplicate = Path.Combine(steamDirectories.Plugin, $"{normalizedId}.lua");
        if (File.Exists(duplicate) && !force)
        {
            return ErrorPlan(
                verification.Path,
                normalizedId,
                $"AppID already exists at {duplicate}. Enable overwrite to replace it.");
        }

        var planId = Guid.NewGuid();
        var workingDirectory = Path.Combine(paths.TemporaryRoot, $"appid-{normalizedId}-{planId:N}");
        Directory.CreateDirectory(workingDirectory);
        var attempted = new List<string>();

        for (var index = 0; index < Repositories.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repository = Repositories[index];
            var archivePath = Path.Combine(workingDirectory, $"repository-{index}.zip");
            var extractDirectory = Path.Combine(workingDirectory, $"repository-{index}");
            var url = $"{repository}/archive/refs/heads/{normalizedId}.zip";
            attempted.Add(repository);

            try
            {
                if (!await DownloadAsync(url, archivePath, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                await zipExtractor.ExtractAsync(archivePath, extractDirectory, cancellationToken)
                    .ConfigureAwait(false);
                var plan = await BuildRepositoryPlanAsync(
                    planId,
                    verification.Path,
                    normalizedId,
                    repository,
                    workingDirectory,
                    extractDirectory,
                    steamDirectories,
                    cancellationToken).ConfigureAwait(false);
                if (plan.Entries.Count > 0)
                {
                    return plan;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException
                                                 or InvalidDataException
                                                 or IOException
                                                 or UnauthorizedAccessException)
            {
                LogRepositoryFailed(logger, repository, normalizedId, exception.Message);
            }
        }

        TryDeleteDirectory(workingDirectory);
        return new InjectionPlan
        {
            Id = planId,
            SourceKind = InjectionSourceKind.AppIdBundle,
            AppId = normalizedId,
            SteamPath = verification.Path,
            Errors = [$"AppID {normalizedId} was not found in any configured repository."],
            Warnings = [$"Repositories checked: {string.Join(", ", attempted)}"]
        };
    }

    private async Task<bool> DownloadAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Project-Lightning");
        request.Headers.Accept.ParseAdd("application/zip");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return false;
        }

        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            return false;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
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
            if (total > MaximumDownloadBytes)
            {
                return false;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return total > 0;
    }

    private static async Task<InjectionPlan> BuildRepositoryPlanAsync(
        Guid planId,
        string steamPath,
        string appId,
        string repository,
        string workingDirectory,
        string extractDirectory,
        SteamDirectories steamDirectories,
        CancellationToken cancellationToken)
    {
        var rule = RepositoryContentRules.Resolve(repository);
        var entries = new List<InjectionPlanEntry>();
        var errors = new List<string>();
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transformedDirectory = Path.Combine(workingDirectory, "transformed");
        var sourceFiles = Directory.EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var source in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(source).ToLowerInvariant();
            if (extension is not ".lua" and not ".manifest")
            {
                continue;
            }

            if (extension == ".manifest" && !RepositoryContentRules.AllowsManifest(rule))
            {
                continue;
            }

            var effectiveSource = source;
            if (extension == ".lua" && rule != RepositoryRule.ProjectLightning)
            {
                Directory.CreateDirectory(transformedDirectory);
                var text = await File.ReadAllTextAsync(source, cancellationToken).ConfigureAwait(false);
                effectiveSource = Path.Combine(
                    transformedDirectory,
                    $"{entries.Count}-{Path.GetFileName(source)}");
                await File.WriteAllTextAsync(
                    effectiveSource,
                    RepositoryContentRules.TransformLua(text, rule),
                    cancellationToken).ConfigureAwait(false);
            }

            if (rule == RepositoryRule.Unsupported)
            {
                continue;
            }

            var type = extension == ".lua" ? InjectionFileType.Lua : InjectionFileType.Manifest;
            var targetDirectories = type == InjectionFileType.Lua
                ? new[] { steamDirectories.Plugin, steamDirectories.Lua }
                : new[] { steamDirectories.DepotCache };
            foreach (var targetDirectory in targetDirectories)
            {
                var destination = Path.Combine(targetDirectory, Path.GetFileName(source));
                if (!destinations.Add(destination))
                {
                    errors.Add($"The repository contains duplicate destination {destination}.");
                    continue;
                }

                entries.Add(new InjectionPlanEntry(
                    effectiveSource,
                    $"{repository.Split('/').Last()} > {Path.GetRelativePath(extractDirectory, source)}",
                    destination,
                    type,
                    new FileInfo(effectiveSource).Length,
                    File.Exists(destination)));
            }
        }

        return new InjectionPlan
        {
            Id = planId,
            SourceKind = InjectionSourceKind.AppIdBundle,
            AppId = appId,
            Repository = repository,
            SteamPath = steamPath,
            TemporaryDirectory = workingDirectory,
            Entries = entries,
            Errors = errors,
            Warnings =
            [
                "The bundle is remote content. Review its source and destination list before execution."
            ]
        };
    }

    private static InjectionPlan ErrorPlan(string steamPath, string appId, string error) =>
        new()
        {
            SourceKind = InjectionSourceKind.AppIdBundle,
            AppId = appId,
            SteamPath = steamPath,
            Errors = [error]
        };

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(LogLevel.Debug, "Repository {Repository} failed for AppID {AppId}: {Reason}")]
    private static partial void LogRepositoryFailed(
        ILogger logger,
        string repository,
        string appId,
        string reason);
}
