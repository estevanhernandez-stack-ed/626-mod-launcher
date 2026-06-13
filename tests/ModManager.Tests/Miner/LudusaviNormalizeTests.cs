using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class LudusaviNormalizeTests
{
    [Fact]
    public void Maps_name_and_steam_id_into_a_candidate()
    {
        var games = new[]
        {
            new LudusaviGame("Elden Ring") { SteamAppId = "1245620", SavePaths = new[] { "<home>/EldenRing" } },
        };

        var entries = LudusaviNormalize.ToCandidates(games);

        var e = Assert.Single(entries);
        Assert.Equal("elden-ring", e.Id);            // slug from name
        Assert.Equal("Elden Ring", e.Name);
        Assert.Equal("1245620", e.Stores.SteamAppId);
        Assert.Null(e.Engine);                        // Ludusavi has no engine
        Assert.Null(e.ModPath);                       // nor mod path
        Assert.Contains("ludusavi", e.Provenance.Sources);
    }

    [Fact]
    public void Skips_entries_without_a_steam_id()
    {
        // No Steam id -> we can't key/verify it; drop from candidates (Steam is our only probe today).
        var games = new[] { new LudusaviGame("Some GOG-only Game") { SteamAppId = null } };
        Assert.Empty(LudusaviNormalize.ToCandidates(games));
    }

    [Fact]
    public void Slugs_collide_safely_by_appending_the_app_id()
    {
        var games = new[]
        {
            new LudusaviGame("Game") { SteamAppId = "1" },
            new LudusaviGame("Game!") { SteamAppId = "2" }, // slugifies to the same base "game"
        };

        var ids = LudusaviNormalize.ToCandidates(games).Select(e => e.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count()); // unique ids, no collision
    }

    [Fact]
    public void Derives_a_save_dir_hint_when_present()
    {
        var games = new[] { new LudusaviGame("X") { SteamAppId = "9", SavePaths = new[] { "<home>/X/Saves" } } };
        var e = Assert.Single(LudusaviNormalize.ToCandidates(games));
        Assert.False(string.IsNullOrEmpty(e.SaveDirHint));
    }
}
