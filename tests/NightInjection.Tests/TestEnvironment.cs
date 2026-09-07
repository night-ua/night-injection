using NightInjection.Infrastructure.Configuration;

namespace NightInjection.Tests;

internal sealed class TestEnvironment : IDisposable
{
    public TestEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "night-injection-tests", Guid.NewGuid().ToString("N"));
        Data = Path.Combine(Root, "data");
        Temp = Path.Combine(Root, "temp");
        Steam = Path.Combine(Root, "Steam");
        Directory.CreateDirectory(Steam);
        File.WriteAllText(Path.Combine(Steam, "steam.exe"), string.Empty);
        Paths = new AppPathService(Data, Temp);
    }

    public string Root { get; }
    public string Data { get; }
    public string Temp { get; }
    public string Steam { get; }
    public AppPathService Paths { get; }

    public string CreateFile(string relative, string content = "content")
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(handler(request));
    }
}
