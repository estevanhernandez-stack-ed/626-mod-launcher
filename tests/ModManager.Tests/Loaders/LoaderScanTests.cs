using System.IO;
using ModManager.Core.Loaders;

namespace ModManager.Tests.Loaders;

public class LoaderScanTests
{
    private static string TempPlayFolder(params string[] files)
    {
        var d = Path.Combine(Path.GetTempPath(), "mm-loaders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllText(Path.Combine(d, f), "x");
        return d;
    }

    [Fact]
    public void Detect_finds_modengine2_when_its_launcher_is_present()
    {
        var dir = TempPlayFolder("modengine2_launcher.exe");
        try
        {
            var found = LoaderScan.Detect(dir, "fromsoft", "1245620");
            var d = Assert.Single(found);
            Assert.Equal("mod-engine-2", d.Loader.LoaderId);
            Assert.Equal(Path.Combine(dir, "modengine2_launcher.exe"), d.LauncherPath);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Detect_returns_empty_for_wrong_engine_or_missing_launcher()
    {
        var dir = TempPlayFolder("eldenring.exe");
        try
        {
            Assert.Empty(LoaderScan.Detect(dir, "fromsoft", "1245620")); // no loader exe present
            Assert.Empty(LoaderScan.Detect(dir, "bethesda", "1245620")); // wrong engine
            Assert.Empty(LoaderScan.Detect(null, "fromsoft", "1245620")); // null play folder
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Detect_finds_modengine2_for_any_fromsoft_game_not_just_elden_ring()
    {
        var dir = TempPlayFolder("modengine2_launcher.exe");
        try
        {
            // Dark Souls III appid — ME2 is engine-wide, so it surfaces here too.
            var found = LoaderScan.Detect(dir, "fromsoft", "374320");
            var d = Assert.Single(found);
            Assert.Equal("mod-engine-2", d.Loader.LoaderId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Seamless_stays_elden_ring_only_for_non_ER_fromsoft_games()
    {
        var dir = TempPlayFolder("launch_elden_ring_seamlesscoop.exe");
        try
        {
            // Seamless is pinned to Elden Ring (1245620); a different fromsoft appid must not surface it.
            Assert.Empty(LoaderScan.Detect(dir, "fromsoft", "374320"));
            // BanSafeFor for a non-ER fromsoft game returns ME2 (engine-wide) but not Seamless.
            var safe = LoaderScan.BanSafeFor("fromsoft", "374320");
            Assert.Contains(safe, l => l.LoaderId == "mod-engine-2");
            Assert.DoesNotContain(safe, l => l.LoaderId == "seamless-coop");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void BanSafeFor_lists_the_games_ban_safe_loaders_regardless_of_install()
    {
        var safe = LoaderScan.BanSafeFor("fromsoft", "1245620");
        Assert.Contains(safe, l => l.LoaderId == "mod-engine-2");
        Assert.Contains(safe, l => l.LoaderId == "seamless-coop");
        Assert.All(safe, l => Assert.True(l.BanSafe));
        Assert.Empty(LoaderScan.BanSafeFor("bethesda", "377160")); // none scoped to Fallout 4
    }
}
