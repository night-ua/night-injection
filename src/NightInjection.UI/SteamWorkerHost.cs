using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NightInjection.Infrastructure.DependencyInjection;
using NightInjection.Infrastructure.SteamBridge;

namespace NightInjection.UI;

internal static class SteamWorkerHost
{
    public static async Task RunAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddNightInjectionInfrastructure();
        builder.Services.AddSingleton<BackgroundImportCoordinator>();
        builder.Services.AddSingleton<SteamBridgePipeServer>();
        using var host = builder.Build();
        await host.StartAsync().ConfigureAwait(false);
        try { await host.Services.GetRequiredService<SteamBridgePipeServer>().RunAsync().ConfigureAwait(false); }
        finally { await host.StopAsync().ConfigureAwait(false); }
    }
}
