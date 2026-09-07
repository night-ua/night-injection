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
