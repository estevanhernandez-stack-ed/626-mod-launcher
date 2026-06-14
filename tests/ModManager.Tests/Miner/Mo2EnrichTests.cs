using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class Mo2EnrichTests
{
    private static GameManifest Backbone(params (string id, string steamId)[] games) => new()
    {
        Games = games.Select(g => new GameManifestEntry
        {
            Id = g.id, Name = g.id, Stores = new StoreIds { SteamAppId = g.steamId },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
        }).ToList(),
    };

    [Fact]
    public void Fills_modPath_but_not_engine_for_a_matched_game()
    {
        var backbone = Backbone(("skyrim-se", "489830"));
        var mo2 = new[] { new Mo2Game("Skyrim SE") { SteamIds = new[] { "489830" }, DataPath = "Data" } };

        var result = Mo2Enrich.Apply(backbone, mo2);

        var e = result.Games.Single(g => g.Id == "skyrim-se");
        Assert.Equal("Data", e.ModPath);
        Assert.Null(e.Engine);                               // MO2 never sets engine — only curated overrides do
        Assert.Contains("mo2", e.Provenance.Sources);        // provenance records the enrichment
        Assert.Contains("ludusavi", e.Provenance.Sources);   // original source preserved
    }

    [Fact]
    public void Matches_on_any_steam_id_in_the_mo2_list()
    {
        var backbone = Backbone(("witcher-3", "292030"));
        var mo2 = new[] { new Mo2Game("Witcher 3") { SteamIds = new[] { "499450", "292030" }, DataPath = "Mods" } };

        var e = Mo2Enrich.Apply(backbone, mo2).Games.Single(g => g.Id == "witcher-3");
        Assert.Equal("Mods", e.ModPath);
        Assert.Null(e.Engine); // MO2 never sets engine
    }

    [Fact]
    public void Leaves_unmatched_backbone_entries_untouched()
    {
        var backbone = Backbone(("a", "111"), ("b", "222"));
        var mo2 = new[] { new Mo2Game("A") { SteamIds = new[] { "111" }, DataPath = "Data" } };

        var result = Mo2Enrich.Apply(backbone, mo2);
        var b = result.Games.Single(g => g.Id == "b");
        Assert.Null(b.ModPath);
        Assert.DoesNotContain("mo2", b.Provenance.Sources);
    }

    [Fact]
    public void Does_not_set_modPath_when_data_path_is_empty()
    {
        var backbone = Backbone(("valheim", "892970"));
        var mo2 = new[] { new Mo2Game("Valheim") { SteamIds = new[] { "892970" }, DataPath = "" } };

        var e = Mo2Enrich.Apply(backbone, mo2).Games.Single(g => g.Id == "valheim");
        Assert.Null(e.ModPath);                          // empty DataPath -> no modPath
        Assert.Contains("mo2", e.Provenance.Sources);     // still records the match
    }
}
