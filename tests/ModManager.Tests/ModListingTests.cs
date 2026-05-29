using ModManager.Core;

namespace ModManager.Tests;

public class ModListingTests
{
    [Fact]
    public void DirectInjectListing_lists_seamless_and_loader_mods_and_drops_bare_loader()
    {
        var game = FromSoftFixture.Build();
        var mods = DirectInjectListing.List(game);
        var names = mods.Select(m => m.Name).ToList();

        Assert.Contains("Seamless Co-op", names);
        Assert.Contains("Adjust The Fov", names);            // loader-run DLL, prettified
        Assert.DoesNotContain("DLL mod loader", names);      // dropped: its mods\ has contents
        Assert.Equal("direct-inject", mods.First(m => m.Name == "Seamless Co-op").Location);
        Assert.Equal("co-op", mods.First(m => m.Name == "Seamless Co-op").Class);
    }

    [Fact]
    public void DirectInjectListing_keeps_bare_loader_when_mods_folder_empty()
    {
        var game = FromSoftFixture.Build();
        Directory.Delete(Path.Combine(game.GameRoot!, "Game", "mods"), recursive: true);
        var names = DirectInjectListing.List(game).Select(m => m.Name).ToList();
        Assert.Contains("DLL mod loader", names);            // no contents -> bare loader row stays
    }

    [Fact]
    public void DirectInjectListing_returns_empty_for_missing_game_folder()
    {
        var game = new GameEntry
        {
            Id = "er", GameName = "ER", Engine = "fromsoft",
            GameRoot = Path.Combine(TestSupport.TempDir("fs-none-"), "DoesNotExist"),
        };
        Assert.Empty(DirectInjectListing.List(game)); // missing folder -> empty list, no throw
    }

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

    [Fact]
    public void Resolve_fromsoft_returns_enriched_direct_inject_mods()
    {
        var game = FromSoftFixture.Build();
        var mods = ModListing.Resolve(game);
        var seamless = mods.First(m => m.Name == "Seamless Co-op");

        Assert.Equal("Seamless Co-op (Elden Ring)", seamless.DisplayName); // enriched from metadata.json
        Assert.Equal("Yui", seamless.Author);
        Assert.Equal("https://www.nexusmods.com/eldenring/mods/510", seamless.ModUrl);
        Assert.Contains(mods, m => m.Name == "Adjust The Fov");
        Assert.DoesNotContain(mods, m => m.Name == "DLL mod loader");
    }

    [Fact]
    public void Resolve_scanner_world_classifies_and_does_not_write()
    {
        var (_, _, _, c) = ScannerCoreTests.SetupPublic();
        var mods = ModListing.Resolve(c.Game);
        Assert.Equal("both", mods.First(m => m.Name == "Cool").Class); // classified
        Assert.False(File.Exists(c.ClassificationPath));               // read-only: no write
    }
}
