using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Networking;

namespace NightInjection.Tests;

public sealed class NodeApiInteropTests
{
    [Fact]
    public async Task RealNodeApiReturnsZipAndEd25519ThatDotNetVerifiesWithoutInternet()
    {
        using var environment = new TestEnvironment();
        // The fixture remains in the source tree; don't copy it away from its paired website.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "fixtures", "mock-api.cjs")))
            directory = directory.Parent;
        var fixture = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Source API fixture not found."), "fixtures", "mock-api.cjs");
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("NIGHT_TEST_NODE") ?? "node")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        start.ArgumentList.Add(fixture);
        using var process = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var portText = await process.StandardOutput.ReadLineAsync(timeout.Token);
            Assert.True(int.TryParse(portText, out var port), portText ?? await process.StandardError.ReadToEndAsync(timeout.Token));
            var handler = new LocalRouteHandler(new Uri($"http://127.0.0.1:{port}/"));
            var publicKey = Convert.ToBase64String(Convert.FromHexString(
                "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"));
            var service = new WebsiteImportService(new HttpClient(handler), environment.Paths,
                new SafeZipExtractor(), NullLogger<WebsiteImportService>.Instance, new PackageSignatureVerifier(publicKey));
            var result = await service.DownloadAsync(new("730", WebsiteImportActivation.DefaultOrigin), timeout.Token);
            Assert.True(result.Succeeded, result.Message);
            Assert.Contains("Ed25519 source signature verified", result.Message);
            Assert.Contains("addappid(730)", await File.ReadAllTextAsync(Assert.Single(result.FilePaths)));
            var missing = await service.DownloadAsync(new("220", WebsiteImportActivation.DefaultOrigin), timeout.Token);
            Assert.False(missing.Succeeded); Assert.Contains("No generated configuration", missing.Message);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
    }

    private sealed class LocalRouteHandler(Uri local) : DelegatingHandler(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(WebsiteImportService.GenerateEndpoint, request.RequestUri);
            request.RequestUri = new Uri(local, "api/generate");
            return base.SendAsync(request, token);
        }
    }
}
