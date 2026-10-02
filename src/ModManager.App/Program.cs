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

        // Close to tray (B1): a launcher hidden in the tray is still running, so starting it again
        // from the Start menu must bring that window back, not open a second one over the same
        // games.json. Only with the setting on; with it off nothing ever hides, and two windows
        // stay possible exactly as before. Before any other startup work: a process about to hand
        // off and exit has no use for it.
        if (RedirectedToRunningInstance()) return;

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

    /// <summary>True when the launcher holding the tray (the "main" key) was handed this activation
    /// (which shows its window), so this process should exit.</summary>
    private static bool RedirectedToRunningInstance()
    {
        AppInstance main;
        AppActivationArguments activation;
        try
        {
            if (!new ModManager.App.Services.AppSettingsService().CloseToTray) return false;
            // The key is held by whichever window has the tray (MainWindow.ApplyCloseToTray) and
            // released when it turns the tray off or quits. Free means no window can be hidden: this
            // call claims it for this launch, which is about to put up its own tray, and starts normally.
            main = AppInstance.FindOrRegisterForKey(App.InstanceKey);
            if (main.IsCurrent) return false;
            activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        }
        catch
        {
            return false;
        }

        // This process was started by the user, so it may bring a window forward; pass that right on,
        // or the restored window opens behind whatever is in front.
        AllowSetForegroundWindow(main.ProcessId);

        // Off this STA thread, which is about to exit. A redirect that is merely SLOW still counts as
        // handed off: it cannot be cancelled, so starting a window here too would end with two
        // launchers once it lands. Only a redirect that fails outright falls through to a normal start.
        try
        {
            Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait(TimeSpan.FromSeconds(10));
            return true;
        }
        catch
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
