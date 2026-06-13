using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class EmbeddedGameManifestTests
{
    [Fact]
    public void Loads_the_sixteen_game_union()
        => Assert.Equal(16, EmbeddedGameManifest.Current.Games.Count);

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
}
