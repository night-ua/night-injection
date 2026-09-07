using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NightInjection.Core.Interfaces;
using NightInjection.Infrastructure.Configuration;
using NightInjection.Infrastructure.Filesystem;
using NightInjection.Infrastructure.Networking;
using NightInjection.Infrastructure.Steam;
using NightInjection.Infrastructure.Storage;

namespace NightInjection.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNightInjectionInfrastructure(
        this IServiceCollection services,
        IAppPathService? pathService = null)
    {
        if (pathService is not null)
        {
            services.AddSingleton(pathService);
        }
        else
        {
            services.TryAddSingleton<IAppPathService, AppPathService>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISteamService, SteamService>();
        services.AddSingleton<IHistoryRepository, HistoryRepository>();
        services.AddSingleton<SafeZipExtractor>();
        services.AddSingleton<IInjectionService, InjectionService>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<ILoaderPlanService, LoaderPlanService>();

        AddClient<IAppIdService, AppIdService>(services, TimeSpan.FromSeconds(35));
        services.AddHttpClient<IWebsiteImportService, WebsiteImportService>(client =>
                client.Timeout = TimeSpan.FromSeconds(75))
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.None,
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            });
        AddClient<ISteamMetadataService, SteamMetadataService>(services, TimeSpan.FromSeconds(12));
        AddClient<ICoverService, CoverService>(services, TimeSpan.FromSeconds(10));
        return services;
    }

    private static void AddClient<TService, TImplementation>(
        IServiceCollection services,
        TimeSpan timeout)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddHttpClient<TService, TImplementation>(client => client.Timeout = timeout)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10)
            });
    }
}
