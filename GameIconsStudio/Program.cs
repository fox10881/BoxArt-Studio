using System;
using Microsoft.Windows.AppLifecycle;

namespace GameIconsStudio;

/// <summary>
/// Custom entry point — replaces the XAML-generated Main so a second launch
/// redirects activation to the running instance instead of opening a new
/// window. The bootstrapper is auto-initialized before this runs
/// (WindowsAppSDKBootstrapInitialize), so AppLifecycle APIs are available.
/// </summary>
public static class Program
{
    private const string InstanceKey = "BoxArtStudioSingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var keyInstance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!keyInstance.IsCurrent)
        {
            // Another instance owns the key — hand it our activation args so
            // it can foreground its window, then exit quietly.
            try
            {
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                keyInstance.RedirectActivationToAsync(activationArgs).AsTask().Wait(5000);
            }
            catch { /* still exit — never open a second window */ }
            return 0;
        }

        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
