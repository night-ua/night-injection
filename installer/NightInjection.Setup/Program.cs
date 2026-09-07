using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NightInjection.Setup;

internal static class Program
{
    private const string ProductName = "Night Injection";
    private const string PayloadName = "NightInjection.Payload.zip";
    private const string UninstallerPayloadName = "NightInjection.Uninstaller.exe";
    private const string AppExecutable = "NightInjection.exe";
    private const string UninstallerExecutable = "Uninstall.exe";
    private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NightInjection";
    private const string ProtocolRegistryKey = @"Software\Classes\night-injection";
    private static string InstallRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "NightInjection");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "Night Injection.lnk");

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "Windows",
        "Start Menu",
        "Programs",
        "Night Injection.lnk");

    [STAThread]
    private static int Main(string[] args)
    {
        NativeMethods.CoInitializeEx(IntPtr.Zero, 0x2);
        try
        {
            if (args.Any(static argument => argument.Equals("/verify", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    ApplicationConfiguration.Initialize();
                    return VerifyPayload() && SetupWizard.VerifyLayout(InstallRoot) ? 0 : 2;
                }
                catch (Exception exception)
                {
                    File.WriteAllText(
                        Path.Combine(Path.GetTempPath(), "NightInjection-Setup-verify.log"),
                        exception.ToString());
                    return 3;
                }
            }

            return Install();
        }
        catch (Exception exception)
        {
            ShowMessage("Setup could not continue", exception.Message, error: true);
            return 1;
        }
        finally
        {
            NativeMethods.CoUninitialize();
        }
    }

    private static int Install()
    {
        if (IsApplicationRunning())
        {
            ShowMessage(
                "Night Injection is running",
                "Close the application, then run Setup again so its files can be updated safely.",
                error: true);
            return 1;
        }

        ApplicationConfiguration.Initialize();
        using var wizard = new SetupWizard(InstallRoot, InstallFiles);
        wizard.ShowDialog();

        if (wizard.InstallSucceeded && wizard.LaunchAfterInstall)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(InstallRoot, AppExecutable))
            {
                WorkingDirectory = InstallRoot,
                UseShellExecute = true
            });
        }

        return wizard.ExitCode;
    }

    private static void InstallFiles(bool createDesktopShortcut)
    {
        var parent = Directory.GetParent(InstallRoot)!.FullName;
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".NightInjection-install-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".NightInjection-backup-{Guid.NewGuid():N}");
        var movedExisting = false;
        var installedNew = false;

        try
        {
            Directory.CreateDirectory(staging);
            ExtractPayload(staging);
            var stagedExecutable = Path.Combine(staging, AppExecutable);
            if (!File.Exists(stagedExecutable))
            {
                throw new InvalidDataException($"The installer payload does not contain {AppExecutable}.");
            }

            ExtractUninstaller(Path.Combine(staging, UninstallerExecutable));
            if (Directory.Exists(InstallRoot))
            {
                Directory.Move(InstallRoot, backup);
                movedExisting = true;
            }

            Directory.Move(staging, InstallRoot);
            installedNew = true;
            ConfigureInstallation(createDesktopShortcut);

            if (movedExisting)
            {
                TryDeleteDirectory(backup);
            }

        }
        catch
        {
            if (installedNew)
            {
                TryDeleteDirectory(InstallRoot);
            }

            if (movedExisting && Directory.Exists(backup) && !Directory.Exists(InstallRoot))
            {
                Directory.Move(backup, InstallRoot);
            }

            throw;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static bool VerifyPayload()
    {
        using var payload = OpenPayload();
        using var uninstaller = OpenUninstallerPayload();
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
        var payloadIsValid = uninstaller.Length > 0
            && archive.Entries.Count > 0
            && archive.Entries.Any(static entry => entry.FullName.Equals(AppExecutable, StringComparison.OrdinalIgnoreCase))
            && archive.Entries.All(static entry => IsSafeRelativePath(entry.FullName));
        if (!payloadIsValid)
        {
            return false;
        }

        var shortcutProbe = Path.Combine(Path.GetTempPath(), $"NightInjection-shortcut-{Guid.NewGuid():N}.lnk");
        try
        {
            CreateShortcut(shortcutProbe, Environment.ProcessPath!);
            return File.Exists(shortcutProbe) && new FileInfo(shortcutProbe).Length > 0;
        }
        finally
        {
            TryDeleteFile(shortcutProbe);
        }
    }

    private static void ExtractPayload(string destinationRoot)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        using var payload = OpenPayload();
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (!IsSafeRelativePath(entry.FullName))
            {
                throw new InvalidDataException($"Unsafe payload entry: {entry.FullName}");
            }

            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Payload entry escapes the installation folder: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static Stream OpenPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName)
        ?? throw new InvalidDataException("The setup payload is missing. Build Setup through build.ps1.");

    private static Stream OpenUninstallerPayload() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(UninstallerPayloadName)
        ?? throw new InvalidDataException("The uninstaller payload is missing. Build Setup through build.ps1.");

    private static void ExtractUninstaller(string destination)
    {
        using var input = OpenUninstallerPayload();
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return !value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => segment is "." or "..");
    }

    private static void ConfigureInstallation(bool createDesktopShortcut)
    {
        var executable = Path.Combine(InstallRoot, AppExecutable);
        CreateShortcut(StartMenuShortcut, executable);
        if (createDesktopShortcut)
        {
            CreateShortcut(DesktopShortcut, executable);
        }
        else
        {
            TryDeleteFile(DesktopShortcut);
        }

        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey, writable: true)
            ?? throw new InvalidOperationException("Could not create the uninstall registration.");
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", "1.1.0");
        key.SetValue("Publisher", "Night Injection");
        key.SetValue("InstallLocation", InstallRoot);
        key.SetValue("DisplayIcon", $"\"{executable}\",0");
        key.SetValue("UninstallString", $"\"{Path.Combine(InstallRoot, UninstallerExecutable)}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", CalculateDirectorySizeInKilobytes(InstallRoot), RegistryValueKind.DWord);

        using var protocol = Registry.CurrentUser.CreateSubKey(ProtocolRegistryKey, writable: true)
            ?? throw new InvalidOperationException("Could not register the Night Injection link protocol.");
        protocol.SetValue(string.Empty, "URL:Night Injection import link");
        protocol.SetValue("URL Protocol", string.Empty);
        using var icon = protocol.CreateSubKey("DefaultIcon", writable: true)
            ?? throw new InvalidOperationException("Could not register the protocol icon.");
        icon.SetValue(string.Empty, $"\"{executable}\",0");
        using var command = protocol.CreateSubKey(@"shell\open\command", writable: true)
            ?? throw new InvalidOperationException("Could not register the protocol command.");
        command.SetValue(string.Empty, $"\"{executable}\" \"%1\"");
    }

    private static void CreateShortcut(string shortcutPath, string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        ShellShortcut.Create(
            shortcutPath,
            executable,
            InstallRoot,
            "Night Injection — Steam configuration toolkit");
    }

    private static int CalculateDirectorySizeInKilobytes(string root)
    {
        var bytes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Sum(static file => new FileInfo(file).Length);
        return (int)Math.Min(int.MaxValue, Math.Max(1, bytes / 1024));
    }

    private static bool IsApplicationRunning() =>
        new[] { "NightInjection", "NightInjection.UI" }
            .SelectMany(Process.GetProcessesByName)
            .Any(static process => process.Id != Environment.ProcessId);

    private static void ShowMessage(string title, string content, bool error = false) =>
        NativeMethods.MessageBox(IntPtr.Zero, content, title, 0x00000000u | (error ? 0x00000010u : 0x00000040u));

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

}

internal static class ShellShortcut
{
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterface = new("0000010B-0000-0000-C000-000000000046");

    public static unsafe void Create(
        string shortcutPath,
        string executable,
        string workingDirectory,
        string description)
    {
        nint shellLink = 0;
        nint persistFile = 0;
        try
        {
            var classId = ShellLinkClass;
            var shellLinkId = ShellLinkInterface;
            Marshal.ThrowExceptionForHR(NativeMethods.CoCreateInstance(
                ref classId,
                IntPtr.Zero,
                0x1,
                ref shellLinkId,
                out shellLink));

            CallStringSetter(shellLink, 20, executable);
            CallStringSetter(shellLink, 9, workingDirectory);
            CallStringSetter(shellLink, 7, description);
            CallIconSetter(shellLink, executable, 0);

            var persistFileId = PersistFileInterface;
            var shellVtable = *(nint**)shellLink;
            var queryInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)shellVtable[0];
            var interfaceId = &persistFileId;
            var output = &persistFile;
            Marshal.ThrowExceptionForHR(queryInterface(shellLink, interfaceId, output));

            var persistVtable = *(nint**)persistFile;
            var save = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)persistVtable[6];
            fixed (char* path = shortcutPath)
            {
                Marshal.ThrowExceptionForHR(save(persistFile, path, 1));
            }
        }
        finally
        {
            Release(persistFile);
            Release(shellLink);
        }
    }

    private static unsafe void CallStringSetter(nint instance, int vtableIndex, string value)
    {
        var vtable = *(nint**)instance;
        var method = (delegate* unmanaged[Stdcall]<nint, char*, int>)vtable[vtableIndex];
        fixed (char* text = value)
        {
            Marshal.ThrowExceptionForHR(method(instance, text));
        }
    }

    private static unsafe void CallIconSetter(nint instance, string path, int index)
    {
        var vtable = *(nint**)instance;
        var method = (delegate* unmanaged[Stdcall]<nint, char*, int, int>)vtable[17];
        fixed (char* text = path)
        {
            Marshal.ThrowExceptionForHR(method(instance, text, index));
        }
    }

    private static unsafe void Release(nint instance)
    {
        if (instance == 0)
        {
            return;
        }

        var vtable = *(nint**)instance;
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[2];
        _ = release(instance);
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(IntPtr window, string text, string caption, uint type);

    [DllImport("ole32.dll")]
    internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    internal static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(
        ref Guid classId,
        IntPtr outer,
        uint context,
        ref Guid interfaceId,
        out nint instance);

}
