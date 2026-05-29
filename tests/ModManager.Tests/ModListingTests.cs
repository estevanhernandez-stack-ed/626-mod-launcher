using ModManager.Core;

namespace ModManager.Tests;

public class ModListingTests
{
    [Fact]
    public void ModEngine2Listing_lists_config_mods_in_order_with_enabled_state()
    {
        var dir = TestSupport.TempDir("me2-");
        var configPath = Path.Combine(dir, "config_eldenring.toml");
        File.WriteAllText(configPath,
            "[extension.mod_loader]\n" +
            "mods = [\n" +
            "    { enabled = true, name = \"Alpha\", path = \"alpha\" },\n" +
            "    { enabled = false, name = \"Beta\", path = \"beta\" }\n" +
            "]\n");
        var game = new GameEntry { Id = "er", GameName = "ER", Engine = "fromsoft", ModEngineConfig = configPath };

        Assert.True(ModEngine2Listing.IsConfigBacked(game));
        var mods = ModEngine2Listing.List(game);
        Assert.Equal(new[] { "Alpha", "Beta" }, mods.Select(m => m.Name).ToArray());
        Assert.True(mods[0].Enabled);
        Assert.False(mods[1].Enabled);
        Assert.Equal("mod engine 2", mods[0].Location);
        Assert.Equal("both", mods[0].Class);
    }
}
