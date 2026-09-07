namespace NightInjection.Core.Models;

public sealed record SteamInstallationStatus(
    bool IsValid,
    string Path,
    string Message,
    string DetectionSource = "None");

public sealed record SteamDirectories(
    string Root,
    string Plugin,
    string Lua,
    string DepotCache,
    string LibraryCache);

public sealed record LoaderPlan(
    bool IsValid,
    string SteamPath,
    IReadOnlyList<string> PlannedActions,
    IReadOnlyList<string> Warnings);
