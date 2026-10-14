using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NightInjection.Core.Models;

namespace NightInjection.Infrastructure.SteamBridge;

[SupportedOSPlatform("windows")]
public sealed class SteamBridgePipeServer(BackgroundImportCoordinator coordinator)
{
    public static string PipeName => "NightInjection.SteamBridge." + WindowsIdentity.GetCurrent().User!.Value;
    public const int MaximumRequestBytes = 16 * 1024;
    public const int MaximumResponseBytes = 64 * 1024;

    public async Task RunAsync(CancellationToken cancellationToken = default, string? pipeName = null)
    {
        pipeName ??= PipeName;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // The lifetime of this named kernel object is the single-worker reservation.
        // No thread-affine mutex ownership is held across awaits.
        using var reservation = new Mutex(false, "Global\\" + pipeName + ".Worker", out var created);
        if (!created) return;
        await coordinator.InitializeAsync(lifetime.Token).ConfigureAwait(false);
        var clients = new List<Task>();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var pipe = LocalUserPipe.Create(pipeName);
                try { await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false); }
                catch { pipe.Dispose(); throw; }
                pipe.ReadMode = PipeTransmissionMode.Message;
                if (!LocalUserPipe.IsOwnLocalClient(pipe)) { pipe.Dispose(); continue; }
                clients.RemoveAll(t => t.IsCompleted);
                clients.Add(ServeAsync(pipe, lifetime));
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { await Task.WhenAll(clients).ConfigureAwait(false); await coordinator.DisposeAsync().ConfigureAwait(false); }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationTokenSource lifetime)
    {
        await using (pipe.ConfigureAwait(false))
        using (var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var shutdown = false;
            try
            {
                using var message = new MemoryStream();
                var buffer = new byte[4096];
                do
                {
                    var read = await pipe.ReadAsync(buffer, requestTimeout.Token).ConfigureAwait(false);
                    if (read == 0) return;
                    if (message.Length + read > MaximumRequestBytes) throw new InvalidDataException("IPC request exceeds its limit.");
                    message.Write(buffer, 0, read);
                } while (!pipe.IsMessageComplete);
                var request = JsonSerializer.Deserialize<SteamBridgeRequest>(message.ToArray(), BackgroundImportCoordinator.Json)
                    ?? throw new JsonException("Empty IPC request.");
                SteamBridgeReply reply;
                if (request.Op == "shutdown") { shutdown = true; reply = new(true, Message: "Background worker is stopping."); }
                else reply = await coordinator.HandleAsync(request, requestTimeout.Token).ConfigureAwait(false);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(reply, BackgroundImportCoordinator.Json);
                if (bytes.Length > MaximumResponseBytes) throw new InvalidDataException("IPC response exceeds its limit.");
                await pipe.WriteAsync(bytes, requestTimeout.Token).ConfigureAwait(false);
                await pipe.FlushAsync(requestTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (pipe.IsConnected && !requestTimeout.IsCancellationRequested)
                    try { await pipe.WriteAsync(Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"VALIDATION_FAILED\",\"message\":\"Invalid or oversized IPC request.\"}"), requestTimeout.Token).ConfigureAwait(false); }
                    catch (IOException) { }
            }
            finally { if (shutdown) lifetime.Cancel(); }
        }
    }
}
