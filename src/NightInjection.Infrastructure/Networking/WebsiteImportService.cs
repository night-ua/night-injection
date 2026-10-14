using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Results;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Filesystem;

namespace NightInjection.Infrastructure.Networking;

public sealed partial class WebsiteImportService(
    HttpClient httpClient,
    IAppPathService paths,
    SafeZipExtractor zipExtractor,
    ILogger<WebsiteImportService> logger,
    PackageSignatureVerifier? signatures = null) : IWebsiteImportService
{
    public static readonly Uri GenerateEndpoint = new(WebsiteImportActivation.DefaultOrigin, "api/generate");
    public const long MaximumDownloadBytes = 100L * 1024 * 1024;
    private const string ClientHeader = "X-Night-Injection-Client";
    private const string ClientVersion = "1";

    public async Task<WebsiteImportResult> DownloadAsync(
        WebsiteImportRequest request,
        CancellationToken cancellationToken = default,
        IProgress<WebsiteImportProgress>? progress = null)
    {
        if (!WebsiteImportActivation.IsCanonicalAppId(request.AppId)
            || !WebsiteImportActivation.IsTrustedOrigin(request.Origin, WebsiteImportActivation.DevelopmentImportsEnabled))
        {
            return WebsiteImportResult.Failure("The import AppID or server origin is not trusted.", ImportErrorCode.VALIDATION_FAILED);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(75));
        var callerToken = cancellationToken;
        cancellationToken = timeout.Token;
        string normalizedId;
        try
        {
            progress?.Report(new("Connecting"));
            normalizedId = AppIdValidator.Normalize(request.AppId);
        }
        catch (ArgumentException exception)
        {
            return WebsiteImportResult.Failure(exception.Message);
        }

        var importRoot = Path.Combine(paths.TemporaryRoot, "website-imports");
        Directory.CreateDirectory(importRoot);
        CleanupStaleImports(importRoot);
        var identifier = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(importRoot, $".{normalizedId}-{identifier}.part");
        var extractionRoot = Path.Combine(importRoot, $"night-lua-{normalizedId}-{identifier}");
        var keepExtraction = false;

        try
        {
            var endpoint = new Uri(request.Origin, "api/generate");
            using var response = await SendGenerationAsync(endpoint, normalizedId, progress, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return WebsiteImportResult.Failure(MessageForStatus(response.StatusCode), CodeForStatus(response.StatusCode), (int)response.StatusCode);
            }
            progress?.Report(new("Preparing package"));

            if (!string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                return WebsiteImportResult.Failure("The generation server returned an unexpected file type.", ImportErrorCode.PACKAGE_INVALID);
            }

            if (response.Content.Headers.ContentLength is <= 0 or > MaximumDownloadBytes)
            {
                return WebsiteImportResult.Failure("The generated package has an invalid size.", ImportErrorCode.PACKAGE_INVALID);
            }

            if (!TryReadSha256Digest(response, out var expectedDigest))
            {
                return WebsiteImportResult.Failure("The generation server did not provide a valid package digest.", ImportErrorCode.PACKAGE_INVALID);
            }

            var actualDigest = await DownloadFileAsync(
                response.Content,
                temporaryPath,
                cancellationToken,
                progress).ConfigureAwait(false);
            progress?.Report(new("Verifying package"));
            if (!CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest))
            {
                return WebsiteImportResult.Failure("The generated package failed its integrity check.", ImportErrorCode.PACKAGE_INVALID);
            }

            var verifier = signatures ?? new PackageSignatureVerifier();
            var authenticated = verifier.RequiresSignature && verifier.Verify(response, normalizedId, actualDigest);
            if (verifier.RequiresSignature && !authenticated)
                return WebsiteImportResult.Failure("The generated package failed its Ed25519 source authentication check.", ImportErrorCode.PACKAGE_INVALID);

            using (var archive = ZipFile.OpenRead(temporaryPath))
            {
                if (archive.Entries.Count == 0)
                {
                    return WebsiteImportResult.Failure("The generated package is empty.", ImportErrorCode.PACKAGE_INVALID);
                }
                ValidatePackageEntries(archive, normalizedId);
            }

            var extracted = await zipExtractor.ExtractAsync(
                temporaryPath,
                extractionRoot,
                cancellationToken).ConfigureAwait(false);
            var supported = extracted.Where(IsSupportedFile).ToArray();
            if (supported.Length == 0)
            {
                return WebsiteImportResult.Failure(
                    "The generated package contains no Lua or manifest files.");
            }

            var selected = await SelectUniqueFilesAsync(supported, cancellationToken).ConfigureAwait(false);
            await ValidateLuaAsync(selected, normalizedId, cancellationToken).ConfigureAwait(false);
            progress?.Report(new("Ready for review"));
            keepExtraction = true;
            return WebsiteImportResult.Success(
                selected,
                extractionRoot,
                $"Extracted {selected.Count} file(s) for AppID {normalizedId} from {request.Origin.Host}. "
                + (authenticated ? "Ed25519 source signature verified." : "HTTPS and digest verified; source signature is not configured."));
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            return WebsiteImportResult.Failure("The generation server took too long to respond. Retry when the service is available.", ImportErrorCode.NETWORK_ERROR);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.DecoderFallbackException)
        {
            LogImportFailed(logger, normalizedId, exception.Message);
            return WebsiteImportResult.Failure($"Package validation failed: {exception.Message}", ImportErrorCode.PACKAGE_INVALID);
        }
        catch (HttpRequestException exception)
        {
            LogImportFailed(logger, normalizedId, exception.Message);
            return WebsiteImportResult.Failure("Could not connect to DarkDevil. Check your connection and retry.", ImportErrorCode.NETWORK_ERROR);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogImportFailed(logger, normalizedId, exception.Message);
            return WebsiteImportResult.Failure("Could not download or save the package. Check free space and retry.", ImportErrorCode.DOWNLOAD_FAILED);
        }
        finally
        {
            TryDelete(temporaryPath);
            if (!keepExtraction)
            {
                TryDeleteDirectory(extractionRoot);
            }
        }
    }

    private async Task<HttpResponseMessage> SendGenerationAsync(Uri endpoint, string appId,
        IProgress<WebsiteImportProgress>? progress, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new("Checking configuration"));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
            request.Headers.TryAddWithoutValidation(ClientHeader, ClientVersion);
            request.Content = JsonContent.Create(new { appId });
            try
            {
                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if (attempt >= 2 || response.StatusCode is not (HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)) return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 2 && !token.IsCancellationRequested) { }
            progress?.Report(new("Retrying connection"));
            await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), token).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string>> SelectUniqueFilesAsync(
        IEnumerable<string> files,
        CancellationToken cancellationToken)
    {
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files.Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            if (!selected.TryGetValue(name, out var existing))
            {
                selected.Add(name, path);
                continue;
            }

            if (!await FilesMatchAsync(existing, path, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidDataException(
                    $"The generated package contains conflicting copies of {name}.");
            }
        }

        return selected.Values.ToArray();
    }

    private static async Task<bool> FilesMatchAsync(
        string first,
        string second,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length)
        {
            return false;
        }

        await using var firstStream = File.OpenRead(first);
        await using var secondStream = File.OpenRead(second);
        var firstHash = await SHA256.HashDataAsync(firstStream, cancellationToken).ConfigureAwait(false);
        var secondHash = await SHA256.HashDataAsync(secondStream, cancellationToken).ConfigureAwait(false);
        return CryptographicOperations.FixedTimeEquals(firstHash, secondHash);
    }

    private static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".lua", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> DownloadFileAsync(
        HttpContent content,
        string destination,
        CancellationToken cancellationToken,
        IProgress<WebsiteImportProgress>? progress)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > MaximumDownloadBytes)
            {
                throw new InvalidDataException("The generated package exceeds the download limit.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            progress?.Report(new("Downloading", total, content.Headers.ContentLength));
        }

        if (content.Headers.ContentLength is long expectedSize && total != expectedSize)
            throw new IOException("The download ended before the advertised package length was received.");
        if (total == 0)
        {
            throw new InvalidDataException("The generated package is empty.");
        }

        return hash.GetHashAndReset();
    }

    private static void ValidatePackageEntries(ZipArchive archive, string appId)
    {
        var foundLua = false;
        long expanded = 0;
        if (archive.Entries.Count > 512) throw new InvalidDataException("Too many files in generated package.");
        foreach (var entry in archive.Entries)
        {
            expanded = checked(expanded + entry.Length);
            if (expanded > 32L * 1024 * 1024) throw new InvalidDataException("Generated package expansion exceeds its limit.");
            var name = entry.FullName;
            if (name.Contains('\\') || name.Length > 240)
                throw new InvalidDataException("Unexpected package path.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                if (name is not ("lua/" or "stplug-in/" or "depotcache/"))
                    throw new InvalidDataException("Unexpected package directory.");
                continue;
            }
            var lua = System.Text.RegularExpressions.Regex.IsMatch(name,
                @"^(?:(?:lua|stplug-in)/)?[a-zA-Z0-9][a-zA-Z0-9._-]{0,199}\.lua$");
            var manifest = System.Text.RegularExpressions.Regex.IsMatch(name,
                @"^(?:depotcache/)?[1-9][0-9]{0,9}(?:_[1-9][0-9]{0,19})?\.manifest$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!lua && !manifest) throw new InvalidDataException("Unexpected file in generated package.");
            if (entry.Length is <= 0 or > 8L * 1024 * 1024) throw new InvalidDataException("Package file is empty or exceeds the safe limit.");
            foundLua |= lua;
        }
        if (!foundLua) throw new InvalidDataException("The package does not identify the requested AppID.");
    }

    private static async Task ValidateLuaAsync(IReadOnlyList<string> files, string appId, CancellationToken token)
    {
        var identified = false;
        // Accept data declarations only; never run Lua to validate it.
        var declaration = new System.Text.RegularExpressions.Regex(
            """^(?:addappid\s*\(\s*([1-9][0-9]{0,9})\s*(?:,\s*[0-9]+\s*)?(?:,\s*(?:"[a-fA-F0-9]+"|'[a-fA-F0-9]+')\s*)?\)|setManifestid\s*\(\s*[1-9][0-9]{0,9}\s*,\s*(?:"[0-9]+"|'[0-9]+'|[0-9]+)\s*(?:,\s*[0-9]+\s*)?\))\s*$""",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        foreach (var file in files.Where(static p => Path.GetExtension(p).Equals(".lua", StringComparison.OrdinalIgnoreCase)))
        {
            var text = await File.ReadAllTextAsync(file, new System.Text.UTF8Encoding(false, true), token).ConfigureAwait(false);
            foreach (var raw in text.Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                var line = raw.Split("--", 2, StringSplitOptions.None)[0].Trim().TrimEnd(';').Trim();
                if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal)) continue;
                var match = declaration.Match(line);
                if (!match.Success) throw new InvalidDataException("Lua contains unsupported executable content.");
                identified |= match.Groups[1].Value == appId;
            }
        }
        if (!identified) throw new InvalidDataException("Lua content does not match the requested AppID.");
    }

    private static bool TryReadSha256Digest(HttpResponseMessage response, out byte[] digest)
    {
        digest = [];
        if (!response.Headers.TryGetValues("Content-Digest", out var values)
            && !response.Content.Headers.TryGetValues("Content-Digest", out values))
        {
            return false;
        }

        var valueList = values.ToArray();
        const string prefix = "sha-256=:";
        if (valueList.Length != 1
            || !valueList[0].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !valueList[0].EndsWith(':'))
        {
            return false;
        }

        try
        {
            digest = Convert.FromBase64String(valueList[0][prefix.Length..^1]);
            return digest.Length == SHA256.HashSizeInBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string MessageForStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => "No generated configuration was found for this AppID.",
        HttpStatusCode.UnprocessableEntity => "This game has no supported Lua/Manifest configuration in the available sources.",
        HttpStatusCode.TooManyRequests => "The generation service is busy. Please wait and try again.",
        HttpStatusCode.ServiceUnavailable => "The generation service is temporarily unavailable.",
        _ => "The generation server could not create this package."
    };

    private static ImportErrorCode CodeForStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => ImportErrorCode.CONFIGURATION_NOT_FOUND,
        HttpStatusCode.UnprocessableEntity => ImportErrorCode.GAME_NOT_SUPPORTED,
        HttpStatusCode.TooManyRequests => ImportErrorCode.RATE_LIMITED,
        _ => ImportErrorCode.SERVER_ERROR,
    };

    private static void CleanupStaleImports(string directory)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            try
            {
                var name = Path.GetFileName(path);
                if (!System.Text.RegularExpressions.Regex.IsMatch(name,
                    @"^(?:night-lua-[1-9][0-9]{0,9}-[a-f0-9]{32}|\.[1-9][0-9]{0,9}-[a-f0-9]{32}\.part)$")
                    || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) continue;
                if (File.GetLastWriteTimeUtc(path) >= cutoff)
                {
                    continue;
                }

                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(LogLevel.Warning, "Website import failed for AppID {AppId}: {Reason}")]
    private static partial void LogImportFailed(ILogger logger, string appId, string reason);
}
