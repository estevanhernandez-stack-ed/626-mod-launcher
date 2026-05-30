using ModManager.Core;

namespace ModManager.Tests;

// The DLL mod loader (Elden Mod Loader = dinput8.dll) stays a visible, distinguished row, and its
// toggle reversibly cascades the whole stack (loader + every mods\*.dll hosted mod) off/on as one
// atomic action. Two halves: (A) listing surfaces + tags the loader row (IsLoader); (B) the pure
// DirectInject.SetLoaderEnabled cascade with refuse-up-front guards + cross-unit rollback.
public class DirectInjectLoaderCascadeTests : IDisposable
{
    // Light play/holding fixture (mirrors DirectInjectToggleTests) for the pure-op cascade tests.
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-cascade-" + Guid.NewGuid().ToString("N"));
    private string Play => Path.Combine(_root, "Game");
    private string Holding => Path.Combine(_root, "holding");
    private string Mods => Path.Combine(Play, "mods");

    public DirectInjectLoaderCascadeTests()
    {
        Directory.CreateDirectory(Mods);
        File.WriteAllText(Path.Combine(Play, "dinput8.dll"), "LOADER");          // the loader (catalog "DLL mod loader")
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "GAME");          // vanilla — must never move
        File.WriteAllText(Path.Combine(Mods, "AdjustTheFov.dll"), "MOD-A");      // hosted mod A
        File.WriteAllText(Path.Combine(Mods, "RemoveVignette.dll"), "MOD-B");    // hosted mod B
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static string[] Files(string dir) => Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()!;

    // ---- (A) Listing: loader row surfaced + tagged ----------------------------------------------

    [Fact]
    public void DirectInjectListing_loader_row_present_when_hosting_mods()
    {
        var game = FromSoftFixture.Build();   // Game/dinput8.dll + Game/mods/AdjustTheFov.dll
        var rows = DirectInjectListing.List(game);

        var loader = rows.SingleOrDefault(r => r.Name == DirectInject.LoaderName);
        Assert.NotNull(loader);                       // the regression: loader row was dropped when mods\ had contents
        Assert.True(loader!.IsLoader);
        Assert.Contains(rows, r => r.Name == "Adjust The Fov");   // hosted mod still surfaced alongside
    }

    [Fact]
    public void DirectInjectListing_hosted_mod_rows_not_tagged_loader()
    {
        var game = FromSoftFixture.Build();
        var rows = DirectInjectListing.List(game);
        Assert.All(rows.Where(r => r.Name != DirectInject.LoaderName), r => Assert.False(r.IsLoader));
    }

    [Fact]
    public void DirectInjectListing_disabled_loader_row_tagged_loader()
    {
        // Disable just the loader unit into the game's holding dir, then list: a disabled loader row
        // must still carry IsLoader=true so its toggle routes through the cascade (turn the stack ON).
        var game = FromSoftFixture.Build();
        var holding = DirectInjectListing.Holding(game);
        var play = DirectInjectListing.PlayFolder(game.GameRoot)!;
        var loaderUnit = DirectInject.Detect(Files(play), Array.Empty<string>())
            .Single(m => m.Name == DirectInject.LoaderName);
        DirectInject.Disable(play, holding, loaderUnit);

        var loaderRow = DirectInjectListing.List(game).Single(r => r.Name == DirectInject.LoaderName);
        Assert.False(loaderRow.Enabled);
        Assert.True(loaderRow.IsLoader);
    }

    // ---- (B) Cascade: SetLoaderEnabled off/on --------------------------------------------------

    [Fact]
    public void SetLoaderEnabled_off_moves_loader_and_all_hosted_to_holding()
    {
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: false);

        Assert.False(File.Exists(Path.Combine(Play, "dinput8.dll")));        // loader gone
        Assert.False(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));   // hosted A gone
        Assert.False(File.Exists(Path.Combine(Mods, "RemoveVignette.dll"))); // hosted B gone
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe")));       // vanilla untouched

        var disabled = DirectInject.ListDisabled(Holding);
        Assert.Contains(disabled, m => m.Name == DirectInject.LoaderName);
        Assert.Equal(3, disabled.Count);  // loader + 2 hosted
    }

    [Fact]
    public void SetLoaderEnabled_on_restores_loader_last_and_clears_holding()
    {
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: false);
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: true);

        Assert.True(File.Exists(Path.Combine(Play, "dinput8.dll")));
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));
        Assert.True(File.Exists(Path.Combine(Mods, "RemoveVignette.dll")));
        Assert.Empty(DirectInject.ListDisabled(Holding));   // holding cleared on the clean path
    }

    [Fact]
    public void SetLoaderEnabled_round_trip_byte_for_byte()
    {
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: false);
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: true);

        Assert.Equal("LOADER", File.ReadAllText(Path.Combine(Play, "dinput8.dll")));
        Assert.Equal("MOD-A", File.ReadAllText(Path.Combine(Mods, "AdjustTheFov.dll")));
        Assert.Equal("MOD-B", File.ReadAllText(Path.Combine(Mods, "RemoveVignette.dll")));
    }

    [Fact]
    public void SetLoaderEnabled_off_no_loader_installed_is_noop()
    {
        File.Delete(Path.Combine(Play, "dinput8.dll"));   // no loader present
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: false);   // must not throw
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));   // nothing moved
        Assert.False(Directory.Exists(Holding));
    }

    [Fact]
    public void SetLoaderEnabled_on_no_holding_is_noop()
    {
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: true);   // empty holding — must not throw
        Assert.True(File.Exists(Path.Combine(Play, "dinput8.dll")));
    }

    // ---- (B) Guards + rollback (the reversibility-critical cases) -------------------------------

    [Fact]
    public void SetLoaderEnabled_off_rollback_when_hosted_mod_locked()
    {
        // Lock a HOSTED mod DLL so the loader (disabled first) moves successfully, then a later unit
        // fails → outer rollback must restore everything to fully-ON, holding emptied.
        var lockedPath = Path.Combine(Mods, "RemoveVignette.dll");
        using (var hold = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => DirectInject.SetLoaderEnabled(Play, Holding, enabled: false));
            Assert.NotNull(ex.InnerException);   // a sharing-violation chain underneath
        }

        // Fully-ON again: every file back in the play folder, holding empty.
        Assert.True(File.Exists(Path.Combine(Play, "dinput8.dll")));
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));
        Assert.True(File.Exists(lockedPath));
        Assert.Empty(DirectInject.ListDisabled(Holding));
    }

    [Fact]
    public void SetLoaderEnabled_off_loader_locked_is_cheap_noop_rollback()
    {
        // Lock the loader (unit 0) — it fails first, no hosted mod ever moves → play folder unchanged.
        var lockedPath = Path.Combine(Play, "dinput8.dll");
        using (var hold = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<InvalidOperationException>(() => DirectInject.SetLoaderEnabled(Play, Holding, enabled: false));
        }

        Assert.True(File.Exists(lockedPath));
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));      // never moved
        Assert.True(File.Exists(Path.Combine(Mods, "RemoveVignette.dll")));
        Assert.Empty(DirectInject.ListDisabled(Holding));
    }

    [Fact]
    public void SetLoaderEnabled_off_refuses_on_stale_holding_dir()
    {
        // A leftover holding dir for one unit (prior crashed op) → refuse up-front, move nothing.
        Directory.CreateDirectory(Path.Combine(Holding, EnginePresets.Slugify("Adjust The Fov")));
        Assert.Throws<InvalidOperationException>(() => DirectInject.SetLoaderEnabled(Play, Holding, enabled: false));
        Assert.True(File.Exists(Path.Combine(Play, "dinput8.dll")));            // untouched
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));
    }

    [Fact]
    public void SetLoaderEnabled_off_refuses_on_slug_collision()
    {
        // Two hosted mods whose prettified names slug identically would share one holding dir and
        // clobber each other's meta → un-restorable. Refuse up-front, move nothing.
        // "AdjustTheFov.dll" -> "Adjust The Fov" -> slug "adjust-the-fov".
        // "adjustthefov.dll" -> "adjustthefov" -> slug "adjustthefov"  (NOT a collision)
        // Force a real collision: two files whose stems prettify+slug to the same string.
        File.Delete(Path.Combine(Mods, "RemoveVignette.dll"));
        File.WriteAllText(Path.Combine(Mods, "Adjust-The-Fov.dll"), "DUP");   // -> "Adjust-The-Fov" -> slug "adjust-the-fov" == AdjustTheFov's
        Assert.Throws<InvalidOperationException>(() => DirectInject.SetLoaderEnabled(Play, Holding, enabled: false));
        Assert.True(File.Exists(Path.Combine(Play, "dinput8.dll")));            // nothing moved
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));
    }

    [Fact]
    public void SetLoaderEnabled_on_collision_skips_a_reappeared_live_copy()
    {
        // Documents Enable's collision-skip under the cascade: if a play-folder copy reappeared at a
        // restore dest, ON must NOT clobber it and must NOT crash — the live copy wins, the rest restore.
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: false);
        Directory.CreateDirectory(Mods);
        var destPath = Path.Combine(Mods, "RemoveVignette.dll");
        File.WriteAllText(destPath, "LIVE-COPY");   // reappeared at the dest before ON

        DirectInject.SetLoaderEnabled(Play, Holding, enabled: true);   // must not throw

        Assert.True(File.Exists(Path.Combine(Play, "dinput8.dll")));        // loader restored
        Assert.True(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));   // other hosted restored
        Assert.Equal("LIVE-COPY", File.ReadAllText(destPath));             // reappeared copy preserved, not clobbered
    }

    [Fact]
    public void SetLoaderEnabled_on_rollback_re_disables_when_a_restore_move_throws()
    {
        // Genuine ON-rollback path: lock the LOADER's file INSIDE holding so its restore-move throws.
        // Loader restores LAST, so hosted mods restore first (succeed), then the loader Enable throws
        // → ON-rollback re-disables the restored hosted mods. End state: fully-OFF (all back in holding),
        // play folder has no restored hosted DLLs. Exercises the rollback catch branch the auditor flagged.
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: false);

        var lockedInHolding = Path.Combine(Holding, EnginePresets.Slugify(DirectInject.LoaderName), "dinput8.dll");
        Assert.True(File.Exists(lockedInHolding));   // sanity: holding layout is what we expect
        using (var hold = new FileStream(lockedInHolding, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => DirectInject.SetLoaderEnabled(Play, Holding, enabled: true));
            Assert.NotNull(ex.InnerException);
        }

        // Rollback re-disabled the hosted mods → no hosted DLL stranded enabled in the play folder.
        Assert.False(File.Exists(Path.Combine(Mods, "AdjustTheFov.dll")));
        Assert.False(File.Exists(Path.Combine(Mods, "RemoveVignette.dll")));
        // The loader (whose move failed) stays in holding — non-destructive, re-converges next toggle.
        Assert.Contains(DirectInject.ListDisabled(Holding), m => m.Name == DirectInject.LoaderName);
    }

    // ---- IsLoader transient ---------------------------------------------------------------------

    [Fact]
    public void Mod_IsLoader_is_transient_not_on_disk()
    {
        // After a cascade-off, the only thing written to disk is each unit's __626mod.json (DisabledMeta).
        // It has no IsLoader field, and Mod is never serialized — so the flag cannot leak to disk.
        DirectInject.SetLoaderEnabled(Play, Holding, enabled: false);
        foreach (var meta in Directory.GetFiles(Holding, "__626mod.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(meta);
            Assert.DoesNotContain("isLoader", json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
