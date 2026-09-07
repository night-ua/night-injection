using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;

namespace NightInjection.Infrastructure.Configuration;

public sealed partial class SettingsService(
    IAppPathService paths,
    ILogger<SettingsService> logger) : ISettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; } = new();

    public async Task<AppSettings> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            paths.EnsureApplicationDirectories();
            if (!File.Exists(paths.SettingsPath))
            {
                Current = WithRuntimeDefaults(new AppSettings());
                return Current;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(paths.SettingsPath, cancellationToken)
                    .ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                var hasExplicitSize = document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("window_width", out _)
                    && document.RootElement.TryGetProperty("window_height", out _);
                var loaded = JsonSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
                Current = WithRuntimeDefaults(Migrate(loaded ?? new AppSettings(), hasExplicitSize));
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                LogSettingsLoadFailed(logger, paths.SettingsPath, exception.Message);
                PreserveCorruptSettings();
                Current = WithRuntimeDefaults(new AppSettings());
            }

            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = $"{paths.SettingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            paths.EnsureApplicationDirectories();
            var normalized = WithRuntimeDefaults(settings with
            {
                SettingsVersion = AppSettings.CurrentSettingsVersion
            });
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(paths.SettingsPath))
            {
                var backup = $"{paths.SettingsPath}.bak";
                try
                {
                    File.Replace(temporary, paths.SettingsPath, backup, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporary, paths.SettingsPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporary, paths.SettingsPath);
            }

            Current = normalized;
        }
        finally
        {
            TryDelete(temporary);
            _gate.Release();
        }
    }

    private AppSettings WithRuntimeDefaults(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.CacheDirectory)
            ? settings with { CacheDirectory = paths.CoversRoot }
            : settings;

    private static AppSettings Migrate(AppSettings settings, bool hasExplicitSize)
    {
        var settingsVersion = settings.SettingsVersion;
        if ((!hasExplicitSize || settings.WindowWidth <= 0 || settings.WindowHeight <= 0)
            && TryParseGeometry(settings.WindowGeometry, out var width, out var height))
        {
            settings = settings with { WindowWidth = width, WindowHeight = height };
        }

        if (settingsVersion < AppSettings.CurrentSettingsVersion
            && settings.WindowWidth == 1280
            && settings.WindowHeight == 800)
        {
            settings = settings with
            {
                WindowGeometry = $"{AppSettings.DefaultWindowWidth}x{AppSettings.DefaultWindowHeight}",
                WindowWidth = AppSettings.DefaultWindowWidth,
                WindowHeight = AppSettings.DefaultWindowHeight
            };
        }

        return settings with
        {
            SettingsVersion = AppSettings.CurrentSettingsVersion,
            WindowWidth = Math.Clamp(settings.WindowWidth, 720, 7680),
            WindowHeight = Math.Clamp(settings.WindowHeight, 520, 4320),
            LastSection = string.IsNullOrWhiteSpace(settings.LastSection) ? "Dashboard" : settings.LastSection
        };
    }

    private static bool TryParseGeometry(string? geometry, out int width, out int height)
    {
        width = AppSettings.DefaultWindowWidth;
        height = AppSettings.DefaultWindowHeight;
        if (string.IsNullOrWhiteSpace(geometry))
        {
            return false;
        }

        var size = geometry.Split('+', 2)[0].Split('x', 2);
        return size.Length == 2
            && int.TryParse(size[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out height);
    }

    private void PreserveCorruptSettings()
    {
        try
        {
            var destination = $"{paths.SettingsPath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(paths.SettingsPath, destination, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(LogLevel.Warning, "Could not load settings from {Path}: {Reason}")]
    private static partial void LogSettingsLoadFailed(ILogger logger, string path, string reason);
}
