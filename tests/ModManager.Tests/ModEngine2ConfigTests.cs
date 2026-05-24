using ModManager.Core;

namespace ModManager.Tests;

// Reading/writing Mod Engine 2's config_*.toml. The hazard: the shipped config has a
// commented-out EXAMPLE `# mods = [ ... # ]` BEFORE the real array, so a naive regex edits the
// comment. These tests pin that we only ever touch the real, uncommented arrays.
public class ModEngine2ConfigTests
{
    // A faithful slice of the real ME2 config_eldenring.toml: comment example precedes the real array.
    private const string Sample = """
# Global mod engine configuration
[modengine]
debug = false

# external_dlls = [ "coolmod.dll", "D:\\nicemods\\nicemod.dll" ]
external_dlls = []

[extension.mod_loader]
enabled = true
loose_params = false

# Example only:
# mods = [
#    { enabled = true, name = "coolmod", path = "mod1" },
#    { enabled = true, name = "nicemod", path = "mod2" }
# ]
mods = [
    { enabled = true, name = "default", path = "mod" }
]

[extension.scylla_hide]
enabled = false
""";

    [Fact]
    public void Parses_only_the_real_mods_array_not_the_comment()
    {
        var mods = ModEngine2Config.ParseMods(Sample);
        Assert.Single(mods);
        Assert.True(mods[0].Enabled);
        Assert.Equal("default", mods[0].Name);
        Assert.Equal("mod", mods[0].Path);
    }

    [Fact]
    public void Parses_multiple_mods_in_order_with_enabled_flags_and_unescaped_paths()
    {
        const string toml = """
[extension.mod_loader]
mods = [
    { enabled = true, name = "ashes", path = "mod\\ashes" },
    { enabled = false, name = "rando", path = "mod\\randomizer" }
]
""";
        var mods = ModEngine2Config.ParseMods(toml);
        Assert.Equal(2, mods.Count);
        Assert.Equal("ashes", mods[0].Name);
        Assert.Equal(@"mod\ashes", mods[0].Path); // \\ unescaped to \
        Assert.False(mods[1].Enabled);
        Assert.Equal(@"mod\randomizer", mods[1].Path);
    }

    [Fact]
    public void WriteMods_replaces_only_the_real_array_and_keeps_the_comment_example()
    {
        var edited = ModEngine2Config.WriteMods(Sample, new[]
        {
            new Me2Mod(false, "default", "mod"),
            new Me2Mod(true, "convergence", @"mod\convergence"),
        });

        // Comment example survives verbatim.
        Assert.Contains("#    { enabled = true, name = \"coolmod\", path = \"mod1\" },", edited);
        // Round-trips to exactly what we wrote, in order.
        var reparsed = ModEngine2Config.ParseMods(edited);
        Assert.Equal(2, reparsed.Count);
        Assert.False(reparsed[0].Enabled);
        Assert.Equal("convergence", reparsed[1].Name);
        Assert.Equal(@"mod\convergence", reparsed[1].Path); // re-escaped on write, unescaped on read
        // Unrelated sections untouched.
        Assert.Contains("[extension.scylla_hide]", edited);
        Assert.Contains("loose_params = false", edited);
    }

    [Fact]
    public void WriteMods_with_no_mods_emits_empty_array()
    {
        var edited = ModEngine2Config.WriteMods(Sample, Array.Empty<Me2Mod>());
        Assert.Contains("mods = []", edited);
        Assert.Empty(ModEngine2Config.ParseMods(edited));
    }

    [Fact]
    public void External_dlls_round_trip()
    {
        Assert.Empty(ModEngine2Config.ParseExternalDlls(Sample));
        var edited = ModEngine2Config.WriteExternalDlls(Sample, new[] { "ersc.dll", @"D:\mods\x.dll" });
        var dlls = ModEngine2Config.ParseExternalDlls(edited);
        Assert.Equal(2, dlls.Count);
        Assert.Equal("ersc.dll", dlls[0]);
        Assert.Equal(@"D:\mods\x.dll", dlls[1]);
        // The commented external_dlls example is not what we parsed.
        Assert.Contains("# external_dlls = [", edited);
    }
}
