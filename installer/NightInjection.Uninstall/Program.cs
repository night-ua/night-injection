using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NightInjection.Uninstall;

internal static class Program
{
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NightInjection";
    private const string ProtocolRegistryKey = @"Software\Classes\night-injection";

    [STAThread]
    private static int Main()
    {
        try
        {
            var expectedRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "NightInjection"));
            var installRoot = Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!installRoot.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                Show("Uninstall could not continue", "This uninstaller is not inside the verified Night Injection installation folder.", true);
                return 1;
            }

            if (new[] { "NightInjection", "NightInjection.UI" }
                .SelectMany(Process.GetProcessesByName)
                .Any())
            {
                Show("Night Injection is running", "Close Night Injection before uninstalling it.", true);
                return 1;
            }

            if (NativeMethods.MessageBox(
                    IntPtr.Zero,
                    "Application files and shortcuts will be removed. Settings, history, cache, and logs will remain in Local AppData.",
                    "Uninstall Night Injection?",
                    0x00000004u | 0x00000020u) != 6)
            {
                return 0;
            }

            DeleteFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Night Injection.lnk"));
            DeleteFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs",
                "Night Injection.lnk"));
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, throwOnMissingSubKey: false);
            RemoveProtocolRegistration(Path.Combine(installRoot, "NightInjection.exe"));

            var currentExecutable = Path.GetFullPath(Environment.ProcessPath!);
            foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
            {
                if (!Path.GetFullPath(file).Equals(currentExecutable, StringComparison.OrdinalIgnoreCase))
                {
                    DeleteFile(file);
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(installRoot, "*", SearchOption.AllDirectories)
                         .OrderByDescending(static value => value.Length))
            {
                DeleteEmptyDirectory(directory);
            }

            NativeMethods.MoveFileEx(currentExecutable, null, 0x4);
            NativeMethods.MoveFileEx(installRoot, null, 0x4);
            Show(
                "Night Injection was removed",
                "Application files and shortcuts were removed. Windows will clean the small uninstaller stub after restart. Your application data was kept.");
            return 0;
        }
        catch (Exception exception)
        {
            Show("Uninstall could not continue", exception.Message, true);
            return 1;
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void RemoveProtocolRegistration(string expectedExecutable)
    {
        using var commandKey = Registry.CurrentUser.OpenSubKey(
            $@"{ProtocolRegistryKey}\shell\open\command",
            writable: false);
        var command = commandKey?.GetValue(string.Empty) as string;
        var expected = $"\"{expectedExecutable}\" \"%1\"";
        if (string.Equals(command, expected, StringComparison.OrdinalIgnoreCase))
        {
            commandKey?.Close();
            Registry.CurrentUser.DeleteSubKeyTree(ProtocolRegistryKey, throwOnMissingSubKey: false);
        }
    }

    private static void DeleteEmptyDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Show(string title, string content, bool error = false) =>
        NativeMethods.MessageBox(IntPtr.Zero, content, title, error ? 0x10u : 0x40u);
}

internal static class NativeMethods
{
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(IntPtr window, string text, string caption, uint type);

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
