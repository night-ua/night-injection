using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Networking;
using NightInjection.Infrastructure.Steam;
using NightInjection.Infrastructure.Storage;

namespace NightInjection.Tests;

public sealed class WebsiteImportTests
{
    [Theory]
    [InlineData("night-injection://import?appId=730", "730")]
    [InlineData("NIGHT-INJECTION://IMPORT?appId=220", "220")]
    public void ActivationLinkAcceptsOnlyTheExpectedShape(string value, string expectedAppId)
    {
        Assert.True(WebsiteImportActivation.TryParse(value, out var request));
        Assert.Equal(expectedAppId, request.AppId);
        Assert.Equal(WebsiteImportActivation.DefaultOrigin, request.Origin);
    }

    [Fact]
    public void ActivationLinkAcceptsAndNormalizesLocalWebsiteOrigin()
    {
        const string value = "night-injection://import?appId=264710&origin=http%3A%2F%2F127.0.0.1%3A3000";

        Assert.True(WebsiteImportActivation.TryParse(value, out var request));
        Assert.Equal("264710", request.AppId);
        Assert.Equal(new Uri("http://127.0.0.1:3000/"), request.Origin);
    }

    [Theory]
    [InlineData("https://darkdevil.space/import?appId=730")]
    [InlineData("night-injection://other?appId=730")]
    [InlineData("night-injection://import?appId=730&url=https://evil.example")]
    [InlineData("night-injection://import?appId=../730")]
    [InlineData("night-injection://user@import?appId=730")]
    [InlineData("night-injection://import?appId=4294967296")]
    [InlineData("night-injection://import?appId=730&origin=https%3A%2F%2Fevil.example")]
    [InlineData("night-injection://import?appId=730&origin=http%3A%2F%2F192.168.1.10%3A3000")]
    public void ActivationLinkRejectsUnexpectedInputs(string value)
    {
        Assert.False(WebsiteImportActivation.TryParse(value, out _));
    }

    [Fact]
    public async Task WebsiteImportDownloadsVerifiedZipFromFixedEndpoint()
    {
        using var environment = new TestEnvironment();
        var zip = CreateZipBytes(
            ("lua/730.lua", "addappid(730)"),
            ("stplug-in/730.lua", "addappid(730)"));
        var digest = Convert.ToBase64String(SHA256.HashData(zip));
        var handler = new StubHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(WebsiteImportService.GenerateEndpoint, request.RequestUri);
            Assert.Equal("1", request.Headers.GetValues("X-Night-Injection-Client").Single());
            Assert.Equal("{\"appId\":\"730\"}", request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zip)
            };
            response.Content.Headers.ContentType = new("application/zip");
            response.Content.Headers.TryAddWithoutValidation("Content-Digest", $"sha-256=:{digest}:");
            return response;
        });
        var service = new WebsiteImportService(
            new HttpClient(handler),
            environment.Paths,
            new SafeZipExtractor(),
            NullLogger<WebsiteImportService>.Instance);

        var result = await service.DownloadAsync(new WebsiteImportRequest(
            "730", WebsiteImportActivation.DefaultOrigin));

        Assert.True(result.Succeeded, result.Message);
        var extracted = Assert.Single(result.FilePaths);
        Assert.Equal("730.lua", Path.GetFileName(extracted));
        Assert.Equal("addappid(730)", await File.ReadAllTextAsync(extracted));
        Assert.Equal(result.TemporaryDirectory, Directory.GetParent(Directory.GetParent(extracted)!.FullName)!.FullName);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.Combine(environment.Temp, "website-imports"), "*", SearchOption.AllDirectories),
            path => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".part", StringComparison.OrdinalIgnoreCase));

        var history = new HistoryRepository(environment.Paths, NullLogger<HistoryRepository>.Instance);
        var injection = new InjectionService(
            environment.Paths,
            new SteamService(),
            history,
            new SafeZipExtractor(),
            TimeProvider.System,
            NullLogger<InjectionService>.Instance);
        var plan = await injection.BuildFilePlanAsync(result.FilePaths, environment.Steam);

        Assert.True(plan.IsValid, string.Join(Environment.NewLine, plan.Errors));
        Assert.Equal(2, plan.Entries.Count);
        Assert.Contains(plan.Entries, entry =>
            entry.DestinationPath.EndsWith("config\\lua\\730.lua", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Entries, entry =>
            entry.DestinationPath.EndsWith("config\\stplug-in\\730.lua", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WebsiteImportRejectsDigestMismatchAndRemovesPartialFile()
    {
        using var environment = new TestEnvironment();
        var zip = CreateZipBytes(("730.lua", "addappid(730)"));
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zip)
            };
            response.Content.Headers.ContentType = new("application/zip");
            response.Headers.TryAddWithoutValidation(
                "Content-Digest",
                $"sha-256=:{Convert.ToBase64String(new byte[32])}:");
            return response;
        });
        var service = new WebsiteImportService(
            new HttpClient(handler),
            environment.Paths,
            new SafeZipExtractor(),
            NullLogger<WebsiteImportService>.Instance);

        var result = await service.DownloadAsync(new WebsiteImportRequest(
            "730", WebsiteImportActivation.DefaultOrigin));

        Assert.False(result.Succeeded);
        Assert.Empty(result.FilePaths);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(environment.Temp, "website-imports")));
    }

    [Fact]
    public async Task WebsiteImportRejectsConflictingCopiesAndCleansExtraction()
    {
        using var environment = new TestEnvironment();
        var zip = CreateZipBytes(
            ("lua/730.lua", "addappid(730)"),
            ("stplug-in/730.lua", "different"));
        var digest = Convert.ToBase64String(SHA256.HashData(zip));
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(zip)
            };
            response.Content.Headers.ContentType = new("application/zip");
            response.Headers.TryAddWithoutValidation("Content-Digest", $"sha-256=:{digest}:");
            return response;
        });
        var service = new WebsiteImportService(
            new HttpClient(handler),
            environment.Paths,
            new SafeZipExtractor(),
            NullLogger<WebsiteImportService>.Instance);

        var result = await service.DownloadAsync(new WebsiteImportRequest(
            "730", WebsiteImportActivation.DefaultOrigin));

        Assert.False(result.Succeeded);
        Assert.Contains("Could not download", result.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(environment.Temp, "website-imports")));
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
