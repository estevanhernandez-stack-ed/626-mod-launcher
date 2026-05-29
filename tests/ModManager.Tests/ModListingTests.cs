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

    [Fact]
    public void ModEngine2Listing_not_config_backed_when_file_missing()
    {
        var game = new GameEntry { Engine = "fromsoft", ModEngineConfig = Path.Combine(TestSupport.TempDir("me2-x-"), "nope.toml") };
        Assert.False(ModEngine2Listing.IsConfigBacked(game));
    }

    [Fact]
    public void ModEngine2Listing_not_config_backed_when_not_fromsoft()
    {
        var dir = TestSupport.TempDir("me2-ng-");
        var path = Path.Combine(dir, "config.toml");
        File.WriteAllText(path, "");
        var game = new GameEntry { Engine = "bepinex", ModEngineConfig = path };
        Assert.False(ModEngine2Listing.IsConfigBacked(game)); // config present but wrong engine
    }
}
