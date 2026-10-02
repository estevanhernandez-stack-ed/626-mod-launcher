using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
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

        // Close to tray (B1): a launcher hidden in the tray is still running, so starting it again
        // from the Start menu must bring that window back, not open a second one over the same
        // games.json. Only with the setting on; with it off nothing ever hides, and two windows
        // stay possible exactly as before.
        if (RedirectedToRunningInstance()) return;

        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);
            _ = new App();
        });
    }

    /// <summary>True when another launcher already owns the "main" key and this activation was
    /// handed to it (which shows its window), so this process should exit.</summary>
    private static bool RedirectedToRunningInstance()
    {
        try
        {
            // Every instance claims the key when it is free, so the first one running always holds it.
            var main = AppInstance.FindOrRegisterForKey("main");
            if (main.IsCurrent) return false;
            if (!new ModManager.App.Services.AppSettingsService().CloseToTray) return false;

            var args = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Off this STA thread, which is about to host XAML or exit. A redirect that doesn't land
            // in time falls through to a normal start: a second window beats no window at all.
            return Task.Run(() => main.RedirectActivationToAsync(args).AsTask()).Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            return false;
        }
    }
}
