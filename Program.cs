using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using Velopack;

namespace McstudDesktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack: handles install/update/uninstall hooks - MUST be first
        VelopackApp.Build().Run();

        Debug.WriteLine("[Program] Starting application");

        // Initialize WinUI 3 application
        Application.Start((p) =>
        {
            Debug.WriteLine("[Program] Application.Start callback invoked");
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            Debug.WriteLine("[Program] SynchronizationContext set");
            new App();
            Debug.WriteLine("[Program] App instance created");
        });

        Debug.WriteLine("[Program] Application.Start returned");
    }
}
