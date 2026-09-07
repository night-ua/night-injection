using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Validation;
using NightInjection.Infrastructure.Configuration;
using NightInjection.Infrastructure.DependencyInjection;
using NightInjection.Infrastructure.Logging;
using NightInjection.UI.Services;
using NightInjection.UI.ViewModels;
using NightInjection.UI.Views;
using Windows.ApplicationModel.Activation;

namespace NightInjection.UI;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;
    public static IServiceProvider Services => ApplicationHost.Services;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    private static IHost ApplicationHost { get; set; } = null!;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly SemaphoreSlim _activationGate = new(1, 1);
    private readonly TaskCompletionSource _startupReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public App()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        var paths = new AppPathService();
        var logProvider = new AppLogProvider(paths);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Logging.AddProvider(logProvider);
        builder.Services.AddSingleton<IAppLogStore>(logProvider);
        builder.Services.AddNightInjectionInfrastructure(paths);
        builder.Services.AddSingleton<IFilePickerService, FilePickerService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<InjectViewModel>();
        builder.Services.AddSingleton<LibraryViewModel>();
        builder.Services.AddSingleton<HistoryViewModel>();
        builder.Services.AddSingleton<LogsViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<DashboardPage>();
        builder.Services.AddSingleton<InjectPage>();
        builder.Services.AddSingleton<LibraryPage>();
        builder.Services.AddSingleton<HistoryPage>();
        builder.Services.AddSingleton<LogsPage>();
        builder.Services.AddSingleton<SettingsPage>();
        builder.Services.AddSingleton<MainWindow>();
        ApplicationHost = builder.Build();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("NIGHT_INJECTION_DATA_ROOT")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "night-injection");
            Directory.CreateDirectory(root);
            File.AppendAllText(
                Path.Combine(root, "startup-crash.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{args.Message}{Environment.NewLine}{args.Exception}{Environment.NewLine}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        await ApplicationHost.StartAsync();
        var settings = Services.GetRequiredService<ISettingsService>();
        var current = await settings.InitializeAsync();
        Services.GetRequiredService<IAppLogStore>().MinimumLevel =
            current.DebugLogging ? LogLevel.Debug : LogLevel.Information;
        var history = Services.GetRequiredService<IHistoryRepository>();
        await history.InitializeAsync();

        if (string.IsNullOrWhiteSpace(current.SteamPath))
        {
            var detected = await Services.GetRequiredService<ISteamService>().DetectAsync(null);
            if (detected.IsValid)
            {
                current = current with { SteamPath = detected.Path };
                await settings.SaveAsync(current);
            }
        }

        var importRequests = GetPendingImportRequests(Program.InitialActivation, Program.InitialProtocolUri);
        Window = Services.GetRequiredService<MainWindow>();
        if (importRequests.Count > 0)
        {
            ((MainWindow)Window).RequestSection("Inject");
        }

        Window.Activate();
        _startupReady.TrySetResult();
        foreach (var request in importRequests)
        {
            await ProcessWebsiteImportAsync(request);
        }
    }

    internal static void QueueActivation(AppActivationArguments args)
    {
        if (Current is not App app)
        {
            return;
        }

        app._dispatcherQueue.TryEnqueue(async () =>
        {
            var requests = GetPendingImportRequests(args, null);
            if (requests.Count == 0)
            {
                await app._startupReady.Task;
                Window.Activate();
                return;
            }

            foreach (var request in requests)
            {
                await app.ProcessWebsiteImportAsync(request);
            }
        });
    }

    private async Task ProcessWebsiteImportAsync(WebsiteImportRequest request)
    {
        await _startupReady.Task;
        await _activationGate.WaitAsync();
        try
        {
            ((MainWindow)Window).RequestSection("Inject");
            Window.Activate();
            await Services.GetRequiredService<InjectViewModel>().ImportFromWebsiteAsync(request);
        }
        finally
        {
            _activationGate.Release();
        }
    }

    private static IReadOnlyList<WebsiteImportRequest> GetPendingImportRequests(
        AppActivationArguments? args,
        string? rawActivation)
    {
        var requests = ActivationHandoff.DequeueAll().ToList();
        var richRequest = GetImportRequest(args);
        if (richRequest is not null)
        {
            requests.Add(richRequest);
        }

        if (WebsiteImportActivation.TryParse(rawActivation, out var rawRequest))
        {
            requests.Add(rawRequest);
        }

        return requests
            .DistinctBy(static request => $"{request.AppId}|{request.Origin.AbsoluteUri}")
            .ToArray();
    }

    private static WebsiteImportRequest? GetImportRequest(AppActivationArguments? args)
    {
        if (args is null)
        {
            return null;
        }

        var value = args.Kind switch
        {
            ExtendedActivationKind.Protocol when args.Data is IProtocolActivatedEventArgs protocol =>
                protocol.Uri.AbsoluteUri,
            ExtendedActivationKind.Launch when args.Data is ILaunchActivatedEventArgs launch =>
                launch.Arguments,
            _ => null
        };
        return WebsiteImportActivation.TryParse(value, out var request) ? request : null;
    }

    public static async Task ShutdownAsync()
    {
        if (ApplicationHost is not null)
        {
            await ApplicationHost.StopAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            ApplicationHost.Dispose();
        }
    }
}
