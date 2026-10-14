using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Results;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Networking;

namespace NightInjection.Tests;

public sealed class ImportCompatibilityV2Tests
{
    [Fact]
    public async Task BodycamZipStaysBinaryAndAppIdPropagatesThroughRequestAndValidation()
    {
        using var e = new TestEnvironment(); var stages = new List<string>();
        var handler = new StubHttpHandler(request => {
            Assert.Contains("\"appId\":\"2406770\"", request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return WebsiteImportSecurityTests.Response(WebsiteImportSecurityTests.Zip("lua/2406770.lua", "addappid(2406770)\naddappid(2406771,0,\"abc123\")"));
        });
        var result = await Service(e, handler).DownloadAsync(new("2406770", WebsiteImportActivation.DefaultOrigin),
            progress: new InlineProgress(p => stages.Add(p.Stage)));
        Assert.True(result.Succeeded, result.Message); Assert.Single(result.FilePaths);
        Assert.Contains("addappid(2406770)", await File.ReadAllTextAsync(result.FilePaths[0]));
        Assert.Contains("Downloading", stages); Assert.Contains("Verifying package", stages);
    }

    [Theory]
    [InlineData("over-the-hill.lua", "  addappid ( 2929250 ) ; -- comment\n")]
    [InlineData("lua/2929250.lua", "addappid(2929250, 1, 'abcdef'); -- inline comment\nsetManifestid ( 2929251, '123456789' )\n")]
    public async Task GeneratorCompatibleNamesWhitespaceAndCommentsDoNotFailValidation(string name, string lua)
    {
        using var e = new TestEnvironment();
        var handler = new StubHttpHandler(_ => WebsiteImportSecurityTests.Response(WebsiteImportSecurityTests.Zip(name, lua)));
        var result = await Service(e, handler).DownloadAsync(new("2929250", WebsiteImportActivation.DefaultOrigin));
        Assert.True(result.Succeeded, result.Message); Assert.Single(result.FilePaths);
    }
    [Theory]
    [InlineData(404, ImportErrorCode.CONFIGURATION_NOT_FOUND, 1)]
    [InlineData(422, ImportErrorCode.GAME_NOT_SUPPORTED, 1)]
    [InlineData(429, ImportErrorCode.RATE_LIMITED, 1)]
    [InlineData(500, ImportErrorCode.SERVER_ERROR, 3)]
    public async Task PermanentAndTransientHttpFailuresHaveDistinctCodesAndBoundedRetries(int status, ImportErrorCode code, int expectedRequests)
    {
        using var e = new TestEnvironment(); var handler = new StubHttpHandler(_ => new((HttpStatusCode)status));
        var result = await Service(e, handler).DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin));
        Assert.False(result.Succeeded); Assert.Equal(code, result.ErrorCode);
        Assert.Equal(status, result.HttpStatus); Assert.Equal(expectedRequests, handler.CallCount);
    }
    [Fact]
    public async Task TransientServerFailureCanRecoverAndReportsRetryWork()
    {
        using var e = new TestEnvironment(); var calls = 0; var stages = new List<string>();
        var handler = new StubHttpHandler(_ => ++calls == 1 ? new(HttpStatusCode.BadGateway)
            : WebsiteImportSecurityTests.Response(WebsiteImportSecurityTests.Zip("730.lua", "addappid(730)")));
        var result = await Service(e, handler).DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin),
            progress: new InlineProgress(value => stages.Add(value.Stage)));
        Assert.True(result.Succeeded); Assert.Equal(2, calls);
        Assert.Contains("Retrying connection", stages); Assert.Contains("Preparing package", stages);
    }
    [Fact]
    public async Task HeaderTimeoutIsANetworkFailureRatherThanEndlessConnecting()
    {
        using var e = new TestEnvironment();
        var client = new HttpClient(new SlowHandler()) { Timeout = TimeSpan.FromMilliseconds(30) };
        var service = new WebsiteImportService(client, e.Paths, new SafeZipExtractor(), NullLogger<WebsiteImportService>.Instance, new PackageSignatureVerifier(string.Empty));
        var result = await service.DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin));
        Assert.False(result.Succeeded); Assert.Equal(ImportErrorCode.NETWORK_ERROR, result.ErrorCode);
    }

    [Fact]
    public async Task EmptyLuaAndLostNetworkAreClassifiedWithoutReachingApply()
    {
        using var e = new TestEnvironment();
        var empty = new StubHttpHandler(_ => WebsiteImportSecurityTests.Response(WebsiteImportSecurityTests.Zip("730.lua", "")));
        Assert.Equal(ImportErrorCode.PACKAGE_INVALID, (await Service(e, empty).DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin))).ErrorCode);
        var network = new StubHttpHandler(_ => throw new HttpRequestException("mock offline"));
        Assert.Equal(ImportErrorCode.NETWORK_ERROR, (await Service(e, network).DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin))).ErrorCode);
        Assert.Equal(3, network.CallCount);
    }
    private static WebsiteImportService Service(TestEnvironment e, HttpMessageHandler handler) =>
        new(new HttpClient(handler), e.Paths, new SafeZipExtractor(), NullLogger<WebsiteImportService>.Instance, new PackageSignatureVerifier(string.Empty));
    private sealed class InlineProgress(Action<NightInjection.Core.Interfaces.WebsiteImportProgress> callback)
        : IProgress<NightInjection.Core.Interfaces.WebsiteImportProgress>
    { public void Report(NightInjection.Core.Interfaces.WebsiteImportProgress progress) => callback(progress); }
    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }
    }
}
