using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Configuration;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Networking;
using NightInjection.Infrastructure.SteamBridge;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

if (args.FirstOrDefault() == "pipe")
{
    var body = args.Length > 1 ? args[1] : await Console.In.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    using var pipe = new NamedPipeClientStream(".", SteamBridgePipeServer.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(5000, deadline.Token); pipe.ReadMode = PipeTransmissionMode.Message;
    await pipe.WriteAsync(Encoding.UTF8.GetBytes(body), deadline.Token);
    using var message = new MemoryStream(); var buffer = new byte[4096];
    do { var read = await pipe.ReadAsync(buffer, deadline.Token); if (read == 0) break; message.Write(buffer, 0, read); } while (!pipe.IsMessageComplete);
    Console.WriteLine(Encoding.UTF8.GetString(message.ToArray()));
    return;
}
if (args.FirstOrDefault() == "mep")
{
    await MillenniumProbe.RunAsync(args[1]); return;
}
var cursor = new DirectoryInfo(AppContext.BaseDirectory);
while (cursor is not null && (!Directory.Exists(Path.Combine(cursor.FullName, "injector")) || !Directory.Exists(Path.Combine(cursor.FullName, "website")))) cursor = cursor.Parent;
var workspace = cursor?.FullName ?? throw new DirectoryNotFoundException("Integration workspace not found.");
var diagnosticRoot = Path.Combine(workspace, "artifacts", "v2-diagnostics", "dotnet-client");
Directory.CreateDirectory(diagnosticRoot);
var paths = new AppPathService(Path.Combine(diagnosticRoot, "data"), Path.Combine(diagnosticRoot, "temp"));
paths.EnsureApplicationDirectories();
var service = new WebsiteImportService(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }), paths,
    new SafeZipExtractor(), NullLogger<WebsiteImportService>.Instance, new PackageSignatureVerifier(string.Empty));
var records = new List<object>();
foreach (var appId in (args.Length > 0 ? args : new[] { "2929250", "730", "220" }))
{
    var result = await service.DownloadAsync(new(appId, WebsiteImportActivation.DefaultOrigin));
    var record = new { appId, result.Succeeded, result.HttpStatus, errorCode = result.ErrorCode?.ToString(), result.Message,
        files = result.FilePaths.Select(Path.GetFileName).ToArray() };
    records.Add(record); Console.WriteLine(JsonSerializer.Serialize(record));
}
await File.WriteAllTextAsync(Path.Combine(workspace, "artifacts", "v2-diagnostics", "actual-dotnet-client.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
