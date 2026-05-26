using ModManager.Core;

namespace ModManager.Tests;

public class BepInExScanTests
{
    private static GameContext Ctx(string root, bool owned)
    {
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "On.dll"), "x");
        File.WriteAllText(Path.Combine(plugins, "Off.dll.disabled"), "x");
        if (owned) File.WriteAllText(Path.Combine(plugins, "vortex.deployment.x.json"), "{}");
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root, Engine = "bepinex",
            DataDir = Path.Combine(root, "_data"),
            FileExtensions = new[] { "dll" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "plugins", "BepInEx/plugins") },
        });
    }

    [Fact]
    public async Task BuildModList_shows_true_state_and_tags_bepinex_loader()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("bepscan-"), owned: false));
        Assert.True(mods.First(m => m.Name == "On").Enabled);
        Assert.False(mods.First(m => m.Name == "Off").Enabled);   // disabled plugin still listed
        Assert.Equal("bepinex", mods.First(m => m.Name == "On").Loader);
    }

    [Fact]
    public async Task Disabling_a_bepinex_plugin_renames_and_moves_no_files()
    {
        var root = TestSupport.TempDir("bepdis-");
        var c = Ctx(root, owned: false);
        var plugins = Path.Combine(root, "BepInEx", "plugins");

        await Scanner.DisableModAsync("On", c);

        Assert.False(File.Exists(Path.Combine(plugins, "On.dll")));
        Assert.True(File.Exists(Path.Combine(plugins, "On.dll.disabled")));   // renamed, not moved to holding
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "On")));
    }

    [Fact]
    public async Task Enabling_a_bepinex_plugin_renames_back()
    {
        var root = TestSupport.TempDir("bepen-");
        var c = Ctx(root, owned: false);
        await Scanner.EnableModAsync("Off", c);
        Assert.True(File.Exists(Path.Combine(root, "BepInEx", "plugins", "Off.dll")));
    }

    [Fact]
    public async Task Owned_bepinex_folder_reads_state_but_is_read_only()
    {
        var mods = await Scanner.BuildModListAsync(Ctx(TestSupport.TempDir("bepown-"), owned: true));
        var on = mods.First(m => m.Name == "On");
        Assert.True(on.ReadOnly);                 // Vortex/MO2 owns it -> coexist
        Assert.True(on.Enabled);                  // true state still read (non-mutating)
    }
}
