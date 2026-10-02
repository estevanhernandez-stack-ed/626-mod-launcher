using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class EmbeddedGameManifestTests
{
    [Fact]
    public void Loads_the_seventeen_game_union()
        => Assert.Equal(17, EmbeddedGameManifest.Current.Games.Count);

    [Fact]
    public void Elden_ring_resolves_engine_and_nexus_domain()
    {
        var er = EmbeddedGameManifest.Current.Games.Single(g => g.Id == "elden-ring");
        Assert.Equal("fromsoft", er.Engine);
        Assert.Equal("1245620", er.Stores.SteamAppId);
        Assert.Equal("eldenring", er.NexusDomain);
    }

    [Fact]
    public void Nexus_only_games_carry_no_engine()
    {
        var witchfire = EmbeddedGameManifest.Current.Games.Single(g => g.Id == "witchfire");
        Assert.Null(witchfire.Engine);
        Assert.Equal("witchfire", witchfire.NexusDomain);
    }

    [Fact]
    public void Cyberpunk_embedded_entry_carries_its_five_extra_trees_in_order()
    {
        // The embedded snapshot is the floor: a launcher offline, freshly installed or holding a cached
        // feed older than the extraModTrees field still needs to see (and toggle) a mod's other trees.
        var cp = EmbeddedGameManifest.Current.Games.Single(g => g.Id == "cyberpunk-2077");
        var expected = new[]
        {
            "r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins",
            "bin/x64/plugins/cyber_engine_tweaks/mods",
        };
        Assert.Equal(expected, cp.ExtraModTrees);

        // And the validator keeps every one: nothing is dropped as unsafe.
        var result = ManifestValidator.Validate(
            new GameManifest { Games = new[] { cp } }, new HashSet<string> { "custom" });
        Assert.Equal(expected, result.Manifest.Games.Single().ExtraModTrees);
    }
}
