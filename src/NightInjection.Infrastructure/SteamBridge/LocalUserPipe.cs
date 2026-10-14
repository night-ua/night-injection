using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NightInjection.Infrastructure.SteamBridge;

[SupportedOSPlatform("windows")]
internal static class LocalUserPipe
{
    // .NET's ordinary pipe constructor does not set PIPE_REJECT_REMOTE_CLIENTS.
    // Create a local-only message pipe with a protected user/SYSTEM DACL, then
    // wrap the owned handle so async reads/writes still use the .NET pipe implementation.
    public static NamedPipeServerStream Create(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"O:{sid}D:P(A;;GA;;;{sid})(A;;GA;;;SY)", 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipe(@"\\.\pipe\" + name, 3 | 0x40000000,
                4 | 2 | 8, 8, SteamBridgePipeServer.MaximumResponseBytes, SteamBridgePipeServer.MaximumRequestBytes, 0, ref attributes);
            if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { LocalFree(descriptor); }
    }

    public static bool IsOwnLocalClient(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var id) || id == 0) return false;
        var process = OpenProcess(0x1000, false, id);
        if (process == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(process, 8, out var token)) return false;
            using (token)
            using (var client = new WindowsIdentity(token.DangerousGetHandle()))
            using (var own = WindowsIdentity.GetCurrent())
            {
                return client.User == own.User
                    && GetTokenInformation(token, 20, out var clientElevation, 4, out _)
                    && GetTokenInformation(own.AccessToken, 20, out var ownElevation, 4, out _)
                    && clientElevation == ownElevation;
            }
        }
        finally { CloseHandle(process); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, int mode, int type, int instances, int output, int input, int timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint id);
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, out int information, int size, out int needed);
}
