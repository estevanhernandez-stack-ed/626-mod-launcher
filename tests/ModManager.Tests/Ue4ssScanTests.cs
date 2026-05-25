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
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
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
    public async Task Owned_ue4ss_folder_still_reads_true_state_but_is_not_loader_driven()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("ue4ss-own-"), owned: true));
        var off = mods.First(m => m.Name == "Off");
        Assert.False(off.Enabled);   // true state still read (non-mutating)
        Assert.True(off.ReadOnly);   // owned -> read-only
        Assert.Null(off.Loader);     // not loader-driven (we won't write an owned folder)
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
