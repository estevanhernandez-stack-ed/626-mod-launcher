using ModManager.Core;

namespace ModManager.Tests;

// BuildModList shows the TRUE UE4SS enable state (even in an owned folder) and tags unowned
// UE4SS mods with Loader="ue4ss" (Conductor) so the toggle drives the manifest, not a file move.
public class Ue4ssScanTests
{
    private static GameContext Ctx(string root, bool owned)
    {
        var mods = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "On"));
        Directory.CreateDirectory(Path.Combine(mods, "Off"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "On : 1\nOff : 0\n");
        if (owned) File.WriteAllText(Path.Combine(mods, "vortex.deployment.x.json"), "{}");
        // Use a DataDir under root so each test's disabled/profiles dirs are isolated
        // (the default DataDir resolves to the PARENT of root/_626mods/t, which is shared
        // across all tests using id="t" in the same Temp folder).
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            DataDir = Path.Combine(root, "_data"),
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
        });
    }

    [Fact]
    public async Task BuildModList_reflects_true_enabled_state_from_the_manifest()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-on-"), owned: false));
        Assert.True(mods.First(m => m.Name == "On").Enabled);
        Assert.False(mods.First(m => m.Name == "Off").Enabled);
    }

    [Fact]
    public async Task Unowned_ue4ss_mods_are_tagged_loader_ue4ss()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-un-"), owned: false));
        Assert.Equal("ue4ss", mods.First(m => m.Name == "On").Loader);
    }

    [Fact]
    public async Task Owned_ue4ss_folder_still_reads_true_state_and_is_loader_tagged()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-own-"), owned: true));
        var off = mods.First(m => m.Name == "Off");
        Assert.False(off.Enabled);      // true state still read (non-mutating)
        Assert.True(off.ReadOnly);      // owned -> read-only (governs CONTENT ops)
        Assert.Equal("ue4ss", off.Loader); // ALL UE4SS mods carry Loader; ReadOnly guards content separately
    }

    // ── NEW: per-row manifest-flip exception (TDD: write failing, then implement) ──────────────

    [Fact]
    public async Task Owned_ue4ss_mod_is_loader_tagged_and_content_readonly()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-ltro-"), owned: true));
        var on = mods.First(m => m.Name == "On");
        Assert.Equal("ue4ss", on.Loader);  // Loader set for all UE4SS, owned or not
        Assert.True(on.ReadOnly);          // ReadOnly still governs content — owned folder invariant intact
    }

    [Fact]
    public async Task SetLoaderModEnabled_toggles_an_owned_ue4ss_mod_via_manifest_without_moving_content()
    {
        var root = TestSupport.TempDir("ue4ss-flip-");
        var c = Ctx(root, owned: true);
        var modsDir = Path.Combine(root, "ue4ss", "Mods");
        var contentFile = Path.Combine(modsDir, "On", "Scripts", "main.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(contentFile)!);
        File.WriteAllText(contentFile, "-- test lua");

        // Explicit per-row manifest flip — allowed on owned folders (manifest only, never content move).
        await Scanner.SetLoaderModEnabledAsync("On", false, c);

        Assert.False(Ue4ssManifest.IsEnabled(modsDir, "On"));          // manifest flipped
        Assert.True(Directory.Exists(Path.Combine(modsDir, "On")));    // content folder untouched
        Assert.True(File.Exists(contentFile));                          // content file untouched
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "On"))); // nothing in holding
    }

    [Fact]
    public async Task Bulk_SetAllMods_still_skips_owned_ue4ss_mods()
    {
        var root = TestSupport.TempDir("ue4ss-bulk-");
        var c = Ctx(root, owned: true);
        var modsDir = Path.Combine(root, "ue4ss", "Mods");

        // "On" is enabled in mods.txt; bulk disable should NOT flip it (owned = conservative skip).
        await Scanner.SetAllModsAsync(false, c);

        Assert.True(Ue4ssManifest.IsEnabled(modsDir, "On")); // owned mod state UNCHANGED by bulk
    }

    [Fact]
    public async Task Disabling_an_unowned_ue4ss_mod_flips_the_manifest_and_moves_no_files()
    {
        var root = TestSupport.TempDir("ue4ss-dis-");
        var c = Ctx(root, owned: false);
        var modDir = Path.Combine(root, "ue4ss", "Mods", "On");

        await Scanner.DisableModAsync("On", c);

        Assert.True(Directory.Exists(modDir));                       // folder NOT moved
        Assert.False(Ue4ssManifest.IsEnabled(Path.Combine(root, "ue4ss", "Mods"), "On")); // manifest flipped
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "On"))); // nothing in holding
    }

    [Fact]
    public async Task Enabling_an_unowned_ue4ss_mod_flips_the_manifest()
    {
        var root = TestSupport.TempDir("ue4ss-en-");
        var c = Ctx(root, owned: false);
        await Scanner.EnableModAsync("Off", c);
        Assert.True(Ue4ssManifest.IsEnabled(Path.Combine(root, "ue4ss", "Mods"), "Off"));
    }
}
