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
    string LibraryCache)
{
    public IReadOnlyList<string> GetLuaDirectories(LuaInjectionTarget target) => target switch
    {
        LuaInjectionTarget.Plugin => [Plugin],
        LuaInjectionTarget.Lua => [Lua],
        LuaInjectionTarget.Both => [Plugin, Lua],
        _ => [Plugin]
    };
}

public sealed record LoaderPlan(
    bool IsValid,
    string SteamPath,
    IReadOnlyList<string> PlannedActions,
    IReadOnlyList<string> Warnings);
