using NightInjection.Core.Interfaces;

namespace NightInjection.Infrastructure.Configuration;

public sealed class AppPathService : IAppPathService
{
    public AppPathService(string? dataRoot = null, string? temporaryRoot = null)
    {
        var configuredDataRoot = Environment.GetEnvironmentVariable("NIGHT_INJECTION_DATA_ROOT");
        var configuredTemporaryRoot = Environment.GetEnvironmentVariable("NIGHT_INJECTION_TEMP_ROOT");
        DataRoot = Path.GetFullPath(dataRoot ?? configuredDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "night-injection"));
        CacheRoot = Path.Combine(DataRoot, "cache");
        CoversRoot = Path.Combine(CacheRoot, "covers");
        LogsRoot = Path.Combine(DataRoot, "logs");
        DatabasePath = Path.Combine(DataRoot, "night-injection.db");
        SettingsPath = Path.Combine(DataRoot, "settings.json");
        TemporaryRoot = Path.GetFullPath(
            temporaryRoot ?? configuredTemporaryRoot ?? Path.Combine(Path.GetTempPath(), "night-injection"));
    }

    public string DataRoot { get; }
    public string CacheRoot { get; }
    public string CoversRoot { get; }
    public string LogsRoot { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public string TemporaryRoot { get; }

    public void EnsureApplicationDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(CoversRoot);
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(TemporaryRoot);
    }
}
