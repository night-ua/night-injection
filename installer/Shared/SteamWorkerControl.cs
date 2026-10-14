using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace NightInjection.Installation;

internal static class SteamWorkerControl
{
    public static void StopForUpgrade(string expectedExecutable)
    {
        try
        {
            var name = "NightInjection.SteamBridge." + WindowsIdentity.GetCurrent().User!.Value;
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(750);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var processId)) return;
            using var process = Process.GetProcessById((int)processId);
            if (!string.Equals(process.MainModule?.FileName, expectedExecutable, StringComparison.OrdinalIgnoreCase)) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            pipe.WriteAsync(Encoding.UTF8.GetBytes("{\"op\":\"shutdown\"}"), timeout.Token).AsTask().GetAwaiter().GetResult();
            var buffer = new byte[1024];
            pipe.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult();
            process.WaitForExit(5000);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException
            or OperationCanceledException or ArgumentException or System.ComponentModel.Win32Exception) { }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);
}
