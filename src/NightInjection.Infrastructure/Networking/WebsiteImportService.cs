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
    ILogger<WebsiteImportService> logger) : IWebsiteImportService
{
    public static readonly Uri GenerateEndpoint = new(WebsiteImportActivation.DefaultOrigin, "api/generate");
    public const long MaximumDownloadBytes = 100L * 1024 * 1024;
    private const string ClientHeader = "X-Night-Injection-Client";
    private const string ClientVersion = "1";

    public async Task<WebsiteImportResult> DownloadAsync(
        WebsiteImportRequest request,
        CancellationToken cancellationToken = default)
    {
        string normalizedId;
        try
        {
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
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
            httpRequest.Headers.TryAddWithoutValidation(ClientHeader, ClientVersion);
            httpRequest.Content = JsonContent.Create(new { appId = normalizedId });

            using var response = await httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return WebsiteImportResult.Failure(MessageForStatus(response.StatusCode));
            }

            if (!string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "application/zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                return WebsiteImportResult.Failure("The generation server returned an unexpected file type.");
            }

            if (response.Content.Headers.ContentLength is <= 0 or > MaximumDownloadBytes)
            {
                return WebsiteImportResult.Failure("The generated package has an invalid size.");
            }

            if (!TryReadSha256Digest(response, out var expectedDigest))
            {
                return WebsiteImportResult.Failure("The generation server did not provide a valid package digest.");
            }

            var actualDigest = await DownloadFileAsync(
                response.Content,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(expectedDigest, actualDigest))
            {
                return WebsiteImportResult.Failure("The generated package failed its integrity check.");
            }

            using (var archive = ZipFile.OpenRead(temporaryPath))
            {
                if (archive.Entries.Count == 0)
                {
                    return WebsiteImportResult.Failure("The generated package is empty.");
                }
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
            keepExtraction = true;
            return WebsiteImportResult.Success(
                selected,
                extractionRoot,
                $"Extracted {selected.Count} file(s) for AppID {normalizedId} from {request.Origin.Host}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WebsiteImportResult.Failure("The generation server took too long to respond.");
        }
        catch (Exception exception) when (exception is HttpRequestException
                                             or IOException
                                             or InvalidDataException
                                             or UnauthorizedAccessException)
        {
            LogImportFailed(logger, normalizedId, exception.Message);
            return WebsiteImportResult.Failure("Could not download the generated package. Please try again.");
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
        CancellationToken cancellationToken)
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
        }

        if (total == 0)
        {
            throw new InvalidDataException("The generated package is empty.");
        }

        return hash.GetHashAndReset();
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
        HttpStatusCode.TooManyRequests => "The generation service is busy. Please wait and try again.",
        HttpStatusCode.ServiceUnavailable => "The generation service is temporarily unavailable.",
        _ => "The generation server could not create this package."
    };

    private static void CleanupStaleImports(string directory)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            try
            {
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
