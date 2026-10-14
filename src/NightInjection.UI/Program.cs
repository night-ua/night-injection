using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using NightInjection.Core.Validation;

namespace NightInjection.UI;

public static class Program
{
    private const string MainInstanceKey = "NightInjection.Main";

    internal static AppActivationArguments InitialActivation { get; private set; } = null!;
    internal static string? InitialProtocolUri { get; private set; }

    [STAThread]
    public static async Task Main()
    {
        if (Environment.GetCommandLineArgs().Skip(1).SequenceEqual(["--steam-worker"]))
        {
            // No WinUI Application.Start, window, activation or foreground UI in this mode.
            try { await SteamWorkerHost.RunAsync().ConfigureAwait(false); }
            catch (Exception exception)
            {
                var root = Environment.GetEnvironmentVariable("NIGHT_INJECTION_DATA_ROOT") ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "night-injection");
                try { Directory.CreateDirectory(root); File.AppendAllText(Path.Combine(root, "steam-worker-crash.log"), exception + Environment.NewLine); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Environment.ExitCode = 1;
            }
            return;
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var current = AppInstance.GetCurrent();
        InitialActivation = current.GetActivatedEventArgs();
        InitialProtocolUri = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(static value => WebsiteImportActivation.TryParse(value, out _));
        var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
        if (!mainInstance.IsCurrent)
        {
            if (InitialProtocolUri is not null)
            {
                ActivationHandoff.Enqueue(InitialProtocolUri);
            }

            await mainInstance.RedirectActivationToAsync(InitialActivation);
            return;
        }

        mainInstance.Activated += static (_, args) => App.QueueActivation(args);
        Application.Start(initialization =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        SynchronizationContext.SetSynchronizationContext(null);
        await App.ShutdownAsync().ConfigureAwait(false);
    }
}
