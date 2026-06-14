using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Threading;
using Velopack;

namespace ModManager.App;

// Custom entry point so VelopackApp.Build().Run() executes BEFORE WinUI starts. Velopack's
// installer / uninstaller / first-run / restart-after-update hooks fire here based on the
// argv the installer passes the binary — if WinUI spins up first, those hooks never run and
// install/uninstall/auto-update silently break.
//
// Enabled in the csproj via:
//   <StartupObject>ModManager.App.Program</StartupObject>
//   <EnableDefaultXamlAppEntry>false</EnableDefaultXamlAppEntry>
//
// The Application.Start block is what the WinUI SDK's default Main generates — restored here
// verbatim so the rest of the app (DI host, MainWindow, theme) sees exactly the same startup.
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().SetArgs(args).Run();

        // Apply the cached remote game-definition manifest (if enabled) BEFORE WinUI / the facades
        // read it. Verified against the pinned key in Core; no-op when disabled or no cache. Dark
        // until a feed exists.
        ModManager.App.Services.RemoteManifestSource.ApplyCachedAtStartup();

        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);
            _ = new App();
        });
    }
}
