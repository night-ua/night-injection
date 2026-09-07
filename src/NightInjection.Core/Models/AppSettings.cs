using System.Text.Json.Serialization;

namespace NightInjection.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<AppTheme>))]
public enum AppTheme
{
    System,
    Light,
    Dark
}

public sealed record AppSettings
{
    public const int CurrentSettingsVersion = 3;
    public const int DefaultWindowWidth = 1538;
    public const int DefaultWindowHeight = 924;

    [JsonPropertyName("settings_version")]
    public int SettingsVersion { get; init; } = CurrentSettingsVersion;

    [JsonPropertyName("steam_path")]
    public string SteamPath { get; init; } = string.Empty;

    [JsonPropertyName("cache_directory")]
    public string CacheDirectory { get; init; } = string.Empty;

    [JsonPropertyName("animations_enabled")]
    public bool AnimationsEnabled { get; init; } = true;

    [JsonPropertyName("auto_scroll_logs")]
    public bool AutoScrollLogs { get; init; } = true;

    [JsonPropertyName("remember_window_size")]
    public bool RememberWindowSize { get; init; } = true;

    [JsonPropertyName("window_geometry")]
    public string WindowGeometry { get; init; } = "1538x924";

    [JsonPropertyName("window_width")]
    public int WindowWidth { get; init; } = DefaultWindowWidth;

    [JsonPropertyName("window_height")]
    public int WindowHeight { get; init; } = DefaultWindowHeight;

    [JsonPropertyName("window_maximized")]
    public bool WindowMaximized { get; init; }

    [JsonPropertyName("last_section")]
    public string LastSection { get; init; } = "Dashboard";

    [JsonPropertyName("debug_logging")]
    public bool DebugLogging { get; init; }

    [JsonPropertyName("fetch_metadata")]
    public bool FetchMetadata { get; init; } = true;

    [JsonPropertyName("theme")]
    public AppTheme Theme { get; init; } = AppTheme.System;
}
