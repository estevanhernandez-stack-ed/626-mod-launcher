using System.IO;
using System.Threading;
using ModManager.App.Services;
using ModManager.Core;

namespace ModManager.App.NexusValidate.Tests;

// E1 sixth slice: a theme saved outside this window (the agent's apply_theme writes through
// AppSettingsFile.WriteKey from another process) reaches a running window, and the window's own saves
// do not echo back as outside changes. Real %APPDATA% file, so same collection and a raw restore.
[Collection("app-settings-file")]
public class AppSettingsThemeFollowTests
{
    [Fact]
    public void A_theme_saved_outside_the_window_is_raised_once_and_its_own_save_is_not()
    {
        var path = new AppSettingsService().Path;
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        var svc = new AppSettingsService();
        var raised = new List<string>();
        using var got = new ManualResetEventSlim();
        void OnSaved(object? s, string id) { lock (raised) raised.Add(id); got.Set(); }
        svc.ThemeSavedElsewhere += OnSaved;
        try
        {
            svc.SetThemeId("ember");
            svc.WatchForOutsideChanges();

            // Its own save, after the watcher is on: read back as its own id, so nothing is raised.
            svc.SetThemeId("mint");
            Thread.Sleep(400);
            lock (raised) Assert.Empty(raised);

            // The agent's write, as the MCP server makes it.
            AppSettingsFile.WriteKey(path, "themeId", "forge");

            Assert.True(got.Wait(TimeSpan.FromSeconds(5)), "no ThemeSavedElsewhere within 5s");
            Thread.Sleep(300);   // let any duplicate file events arrive
            lock (raised) Assert.Equal(new[] { "forge" }, raised);
            Assert.Equal("forge", svc.ThemeId);   // so picking ember again later is saved, not skipped
        }
        finally
        {
            svc.ThemeSavedElsewhere -= OnSaved;
            if (backup is null) File.Delete(path); else File.WriteAllText(path, backup);
        }
    }
}
