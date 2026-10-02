using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModManager.App.Services;
using ModManager.Core;

namespace ModManager.App.NexusValidate.Tests;

// E1 sixth slice: a theme saved outside this window (the agent's apply_theme writes through
// AppSettingsFile.WriteKey from another process) reaches a running window, and the window's own saves
// do not echo back as outside changes. Real %APPDATA% file, so same collection and a raw restore.
[Collection("app-settings-file")]
public class AppSettingsThemeFollowTests
{
    private static void WithRealFile(Action<string> body)
    {
        var path = new AppSettingsService().Path;
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try { body(path); }
        finally { if (backup is null) File.Delete(path); else AppSettingsFile.WithLock(path, () => File.WriteAllText(path, backup)); }
    }

    [Fact]
    public void A_theme_saved_outside_the_window_is_raised_once_and_its_own_save_is_not()
        => WithRealFile(path =>
        {
            using var svc = new AppSettingsService();
            var raised = 0;
            using var got = new ManualResetEventSlim();
            svc.ThemeSavedElsewhere += (_, _) => { Interlocked.Increment(ref raised); got.Set(); };
            svc.SetThemeId("ember");
            svc.WatchForOutsideChanges();

            // Its own save, after the watcher is on: read back as its own id, so nothing is raised.
            svc.SetThemeId("mint");
            Thread.Sleep(400);
            Assert.Equal(0, Volatile.Read(ref raised));

            // The agent's write, as the MCP server makes it.
            AppSettingsFile.WriteKey(path, "themeId", "forge");

            Assert.True(got.Wait(TimeSpan.FromSeconds(5)), "no ThemeSavedElsewhere within 5s");
            Thread.Sleep(300);   // let any duplicate file events arrive
            Assert.Equal(1, Volatile.Read(ref raised));
            Assert.Equal("forge", svc.ThemeId);   // so picking mint again later is saved, not skipped
        });

    [Fact]
    public void A_pick_that_could_not_be_saved_is_not_taken_back_by_the_next_file_event()
        => WithRealFile(path =>
        {
            using var svc = new AppSettingsService();
            svc.SetThemeId("ember");
            var raised = 0;
            svc.ThemeSavedElsewhere += (_, _) => Interlocked.Increment(ref raised);
            svc.WatchForOutsideChanges();

            // Another writer holds the lock past the timeout, so this window's save of mint fails.
            using var holding = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var holder = Task.Run(() => AppSettingsFile.WithLock(path, () => { holding.Set(); release.Wait(TimeSpan.FromSeconds(10)); }));
            holding.Wait();
            svc.SetThemeId("mint");
            release.Set();
            holder.Wait();
            Assert.Equal("ember", svc.ThemeId);   // only a save that landed moves it

            // Any later write to the file (here another setting) must not read as someone putting
            // ember back over the user's pick.
            AppSettingsFile.WriteKey(path, "backdrop", "mica");
            Thread.Sleep(600);
            Assert.Equal(0, Volatile.Read(ref raised));
        });
}
