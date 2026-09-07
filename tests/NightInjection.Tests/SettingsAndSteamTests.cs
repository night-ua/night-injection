using Microsoft.Extensions.Logging.Abstractions;
using NightInjection.Core.Models;
using NightInjection.Infrastructure.Configuration;
using NightInjection.Infrastructure.Steam;

namespace NightInjection.Tests;

public sealed class SettingsAndSteamTests
{
    [Fact]
    public async Task SettingsRoundTripUsesJsonAndLeavesNoTemporaryFile()
    {
        using var environment = new TestEnvironment();
        var service = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        await service.SaveAsync(new AppSettings
        {
            SteamPath = environment.Steam,
            Theme = AppTheme.Dark,
            FetchMetadata = false,
            WindowWidth = 1440,
            WindowHeight = 900
        });

        var reloaded = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        var result = await reloaded.InitializeAsync();

        Assert.Equal(AppTheme.Dark, result.Theme);
        Assert.False(result.FetchMetadata);
        Assert.Equal(1440, result.WindowWidth);
        Assert.Equal(environment.Paths.CoversRoot, result.CacheDirectory);
        Assert.Empty(Directory.EnumerateFiles(environment.Data, "*.tmp"));
    }

    [Fact]
    public async Task CorruptSettingsArePreservedAndDefaultsAreRestored()
    {
        using var environment = new TestEnvironment();
        environment.Paths.EnsureApplicationDirectories();
        await File.WriteAllTextAsync(environment.Paths.SettingsPath, "{ definitely broken");

        var service = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        var result = await service.InitializeAsync();

        Assert.Equal(AppTheme.System, result.Theme);
        Assert.False(File.Exists(environment.Paths.SettingsPath));
        Assert.Single(Directory.EnumerateFiles(environment.Data, "settings.json.corrupt-*"));
    }

    [Fact]
    public async Task PreviousDefaultWindowSizeMigratesToCurrentDefault()
    {
        using var environment = new TestEnvironment();
        environment.Paths.EnsureApplicationDirectories();
        await File.WriteAllTextAsync(
            environment.Paths.SettingsPath,
            "{\"settings_version\":2,\"window_geometry\":\"1280x800\",\"window_width\":1280,\"window_height\":800}");

        var service = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        var result = await service.InitializeAsync();

        Assert.Equal(AppSettings.DefaultWindowWidth, result.WindowWidth);
        Assert.Equal(AppSettings.DefaultWindowHeight, result.WindowHeight);
        Assert.Equal(AppSettings.CurrentSettingsVersion, result.SettingsVersion);
    }

    [Fact]
    public async Task LegacyWindowGeometryMigratesWhenExplicitSizeIsAbsent()
    {
        using var environment = new TestEnvironment();
        environment.Paths.EnsureApplicationDirectories();
        await File.WriteAllTextAsync(environment.Paths.SettingsPath, "{\"window_geometry\":\"1024x700+10+20\",\"theme\":\"Light\"}");

        var service = new SettingsService(environment.Paths, NullLogger<SettingsService>.Instance);
        var result = await service.InitializeAsync();

        Assert.Equal(1024, result.WindowWidth);
        Assert.Equal(700, result.WindowHeight);
        Assert.Equal(AppTheme.Light, result.Theme);
        Assert.Equal(AppSettings.CurrentSettingsVersion, result.SettingsVersion);
    }

    [Fact]
    public async Task SteamVerificationRequiresSteamExecutableAndNormalizesPath()
    {
        using var environment = new TestEnvironment();
        var service = new SteamService();

        var valid = await service.VerifyAsync(Path.Combine(environment.Steam, "."));
        var invalid = await service.VerifyAsync(environment.Root);

        Assert.True(valid.IsValid);
        Assert.Equal(Path.GetFullPath(environment.Steam), valid.Path);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public void SteamDirectoriesMatchLegacyLocationsWithoutCreatingThem()
    {
        using var environment = new TestEnvironment();
        var directories = new SteamService().GetDirectories(environment.Steam);

        Assert.Equal(Path.Combine(environment.Steam, "config", "stplug-in"), directories.Plugin);
        Assert.Equal(Path.Combine(environment.Steam, "config", "lua"), directories.Lua);
        Assert.Equal(Path.Combine(environment.Steam, "config", "depotcache"), directories.DepotCache);
        Assert.False(Directory.Exists(directories.Plugin));
    }
}
