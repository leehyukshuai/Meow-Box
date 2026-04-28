using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace MeowBox.Controller;

public static class Program
{
    private const string SingleInstanceKey = "MeowBox.Controller";

    [STAThread]
    private static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var keyInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
        if (!keyInstance.IsCurrent)
        {
            var activationArguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activationArguments is not null)
            {
                await keyInstance.RedirectActivationToAsync(activationArguments);
            }

            return;
        }

        keyInstance.Activated += OnActivated;

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        App.RequestWindowActivation();
    }
}
