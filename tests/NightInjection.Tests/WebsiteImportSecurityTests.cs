using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Networking;
using NSec.Cryptography;

namespace NightInjection.Tests;

public sealed class WebsiteImportSecurityTests
{
    [Theory]
    [InlineData("0730")]
    [InlineData("730%26url=evil")]
    [InlineData("+730")]
    [InlineData("0")]
    [InlineData("4294967296")]
    [InlineData("730&appId=220")]
    public void NonCanonicalDeepLinksAreRejected(string id) =>
        Assert.False(WebsiteImportActivation.TryParse($"night-injection://import?appId={id}", out _));

    [Theory]
    [InlineData("220.lua", "addappid(220)")]
    [InlineData("lua/730.lua", "addappid(220)")]
    [InlineData("730.lua", "os.execute('anything')")]
    [InlineData("730.lua", "addappid(730)\nrequire('external')")]
    [InlineData("../730.lua", "addappid(730)")]
    [InlineData("payload.exe", "anything")]
    public async Task UnexpectedPathsAppIdsOrExecutableLuaAreRejected(string name, string content)
    {
        using var environment = new TestEnvironment();
        var zip = Zip(name, content);
        var result = await Service(environment, new StubHttpHandler(_ => Response(zip))).DownloadAsync(
            new("730", WebsiteImportActivation.DefaultOrigin));
        Assert.False(result.Succeeded);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(environment.Temp, "website-imports")));
    }

    [Theory]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    public async Task NetworkStatusesDoNotLeaveFiles(int status)
    {
        using var environment = new TestEnvironment();
        var result = await Service(environment, new StubHttpHandler(_ => new((HttpStatusCode)status)))
            .DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin));
        Assert.False(result.Succeeded);
        Assert.Empty(result.FilePaths);
    }

    [Fact]
    public async Task DirectRequestsCannotOverrideTrustedOrigin()
    {
        using var environment = new TestEnvironment();
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("Must not send"));
        var service = Service(environment, handler);
        foreach (var origin in new[] { "https://evil.example/", "http://127.0.0.1:3000/" })
            Assert.False((await service.DownloadAsync(new("730", new Uri(origin)))).Succeeded);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task PinnedEd25519RejectsMissingWrongAndMismatchedSignatures()
    {
        using var environment = new TestEnvironment();
        using var key = Key.Create(SignatureAlgorithm.Ed25519);
        var verifier = new PackageSignatureVerifier(Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
        var zip = Zip("730.lua", "addappid(730)");
        var digest = SHA256.HashData(zip);
        foreach (var mode in new[] { "valid", "missing", "wrong", "wrong-app" })
        {
            var handler = new StubHttpHandler(_ => {
                var response = Response(zip);
                if (mode != "missing")
                {
                    var signedId = mode == "wrong-app" ? "220" : "730";
                    var signature = mode == "wrong" ? new byte[64]
                        : SignatureAlgorithm.Ed25519.Sign(key, PackageSignatureVerifier.Message(signedId, digest));
                    response.Headers.Add("X-DarkDevil-AppId", signedId);
                    response.Headers.Add("X-DarkDevil-Signature", Convert.ToBase64String(signature));
                }
                return response;
            });
            var result = await Service(environment, handler, verifier).DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin));
            Assert.Equal(mode == "valid", result.Succeeded);
        }
    }

    [Fact]
    public async Task CancelledDownloadsNeverReachReview()
    {
        using var environment = new TestEnvironment();
        using var source = new CancellationTokenSource(); source.Cancel();
        var handler = new StubHttpHandler(_ => Response(Zip("730.lua", "addappid(730)")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(environment, handler)
            .DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin), source.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(environment.Temp, "website-imports")));
    }

    private static WebsiteImportService Service(TestEnvironment e, HttpMessageHandler handler, PackageSignatureVerifier? verifier = null) =>
        new(new HttpClient(handler), e.Paths, new SafeZipExtractor(), NullLogger<WebsiteImportService>.Instance, verifier ?? new PackageSignatureVerifier(string.Empty));

    internal static HttpResponseMessage Response(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new("application/zip");
        response.Headers.Add("Content-Digest", $"sha-256=:{Convert.ToBase64String(SHA256.HashData(bytes))}:");
        return response;
    }

    internal static byte[] Zip(string name, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write(content);
        return stream.ToArray();
    }
}
