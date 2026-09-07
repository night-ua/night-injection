using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;

namespace NightInjection.Infrastructure.Steam;

public sealed class LoaderPlanService(ISteamService steamService) : ILoaderPlanService
{
    private static readonly string[] LoaderFiles = ["dwmapi.dll", "xinput1_4.dll", "OpenSteamTool.dll"];
    private static readonly string[] CleanupFiles = ["steam.cfg", "hid.dll"];

    public LoaderPlan CreatePlan(string steamPath)
    {
        var requestedPath = steamPath ?? string.Empty;
        var valid = File.Exists(Path.Combine(requestedPath, "steam.exe"));
        if (!valid)
        {
            return new LoaderPlan(
                false,
                requestedPath,
                [],
                ["steam.exe was not found. No loader action can be planned."]);
        }

        var directories = steamService.GetDirectories(requestedPath);
        var actions = CleanupFiles
            .Select(file => $"Would remove if present: {Path.Combine(directories.Root, file)}")
            .Concat(LoaderFiles.Select(file => $"Would copy after verification: {Path.Combine(directories.Root, file)}"))
            .Concat([$"Would synchronize plug-in Lua files into: {directories.Lua}"])
            .ToArray();
        return new LoaderPlan(
            true,
            directories.Root,
            actions,
            [
                "Planning only. This application does not download or install loader DLLs.",
                "No PowerShell command or executable is launched."
            ]);
    }
}
