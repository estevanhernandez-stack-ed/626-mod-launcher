using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class OverridesMergeTests
{
    private static GameManifest Backbone(params (string id, string steamId, string? engine)[] games) => new()
    {
        Games = games.Select(g => new GameManifestEntry
        {
            Id = g.id, Name = g.id, Engine = g.engine,
            Stores = new StoreIds { SteamAppId = g.steamId },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
        }).ToList(),
    };

    [Fact]
    public void Override_wins_over_mined_fields_on_a_matched_entry()
    {
        var backbone = Backbone(("skyrim", "72850", null));          // mined: no engine
        var overrides = new[] { new OverrideEntry { SteamAppId = "72850", Engine = "bethesda", ModPath = "Data" } };

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single(g => g.Stores.SteamAppId == "72850");
        Assert.Equal("bethesda", e.Engine);
        Assert.Equal("Data", e.ModPath);
        Assert.Contains("curated", e.Provenance.Sources);
        Assert.Equal("curated", e.Provenance.Status);
    }

    [Fact]
    public void Override_replaces_a_value_the_miner_already_set()
    {
        var backbone = Backbone(("x", "1", "custom"));               // mined: wrong/placeholder engine
        var overrides = new[] { new OverrideEntry { SteamAppId = "1", Engine = "bethesda" } };

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single(g => g.Stores.SteamAppId == "1");
        Assert.Equal("bethesda", e.Engine);                          // override wins (not fill-if-empty)
    }

    [Fact]
    public void Override_for_an_unknown_steam_id_adds_a_new_entry()
    {
        var backbone = Backbone(("a", "1", "bethesda"));
        var overrides = new[]
        {
            new OverrideEntry { SteamAppId = "999", Id = "new-game", Name = "New Game", Engine = "ue-pak", ModPath = "Content/Paks/~mods" },
        };

        var result = OverridesMerge.Apply(backbone, overrides);
        var added = result.Games.Single(g => g.Stores.SteamAppId == "999");
        Assert.Equal("new-game", added.Id);
        Assert.Equal("ue-pak", added.Engine);
        Assert.Contains("curated", added.Provenance.Sources);
        Assert.Equal(2, result.Games.Count);
    }

    [Fact]
    public void Unspecified_override_fields_leave_existing_values_intact()
    {
        var backbone = new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "g", Name = "G", Engine = "bethesda", ModPath = "Data",
                    Stores = new StoreIds { SteamAppId = "5" },
                    Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" } },
                },
            },
        };
        var overrides = new[] { new OverrideEntry { SteamAppId = "5", Featured = 3 } }; // only featured

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single();
        Assert.Equal(3, e.Featured);
        Assert.Equal("bethesda", e.Engine);   // untouched
        Assert.Equal("Data", e.ModPath);       // untouched
    }
}
