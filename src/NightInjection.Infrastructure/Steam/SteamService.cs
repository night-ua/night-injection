using Microsoft.Win32;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;

namespace NightInjection.Infrastructure.Steam;

public sealed class SteamService : ISteamService
{
    private static readonly string[] LegacyCandidates =
    [
        @"C:\Program Files (x86)\Steam",
        @"C:\Program Files\Steam",
        @"D:\Program Files (x86)\Steam",
        @"D:\Program Files\Steam",
        @"C:\Steam",
        @"D:\Steam"
    ];

    public Task<SteamInstallationStatus> DetectAsync(
        string? savedPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsValid(savedPath))
        {
            return Task.FromResult(Valid(savedPath!, "Saved setting"));
        }

        foreach (var candidate in ReadRegistryCandidates().Concat(LegacyCandidates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsValid(candidate))
            {
                var source = LegacyCandidates.Contains(candidate, StringComparer.OrdinalIgnoreCase)
                    ? "Known location"
                    : "Windows registry";
                return Task.FromResult(Valid(candidate, source));
            }
        }

        return Task.FromResult(new SteamInstallationStatus(
            false,
            string.Empty,
            "Steam was not detected. Select the folder that contains steam.exe."));
    }

    public Task<SteamInstallationStatus> VerifyAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(new SteamInstallationStatus(false, string.Empty, "Steam path is empty."));
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Task.FromResult(new SteamInstallationStatus(false, path, "Steam path is not valid."));
        }

        return Task.FromResult(IsValid(normalized)
            ? Valid(normalized, "Manual selection")
            : new SteamInstallationStatus(
                false,
                normalized,
                "steam.exe was not found in the selected directory."));
    }

    public SteamDirectories GetDirectories(string steamPath)
    {
        var root = Path.GetFullPath(steamPath);
        return new SteamDirectories(
            root,
            Path.Combine(root, "config", "stplug-in"),
            Path.Combine(root, "config", "lua"),
            Path.Combine(root, "config", "depotcache"),
            Path.Combine(root, "appcache", "librarycache"));
    }

    private static bool IsValid(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(Path.Combine(Path.GetFullPath(path.Trim()), "steam.exe"));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static SteamInstallationStatus Valid(string path, string source)
    {
        var normalized = Path.GetFullPath(path.Trim());
        return new SteamInstallationStatus(true, normalized, "Steam is ready.", source);
    }

    private static IEnumerable<string> ReadRegistryCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var probes = new (RegistryHive Hive, RegistryView View, string Key, string Value)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default, @"Software\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"Software\Valve\Steam", "InstallPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"Software\Valve\Steam", "InstallPath")
        };

        foreach (var probe in probes)
        {
            string? value = null;
            try
            {
                using var hive = RegistryKey.OpenBaseKey(probe.Hive, probe.View);
                using var key = hive.OpenSubKey(probe.Key);
                value = key?.GetValue(probe.Value) as string;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException)
            {
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }
}
