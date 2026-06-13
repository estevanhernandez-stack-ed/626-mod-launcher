using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// Proves the facades read the EFFECTIVE manifest (embedded + remote), not just the embedded one.
// In the DisableParallelization "ManifestState" collection so SetRemote never races other tests.
[Collection("ManifestState")]
public class FacadeRemoteWiringTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static GameManifest Remote(params GameManifestEntry[] games) => new() { Games = games };

    [Fact]
    public void KnownEngines_reflects_a_remote_added_game()
    {
        // an app id not in the embedded snapshot
        Assert.Null(KnownEngines.ByAppId("70000001")); // embedded baseline: unknown

        EffectiveManifest.SetRemote(Remote(new GameManifestEntry
        {
            Id = "remote-bethesda-game",
            Name = "Remote Bethesda Game",
            Engine = "bethesda",
            Stores = new StoreIds { SteamAppId = "70000001" },
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
        }));

        Assert.Equal("bethesda", KnownEngines.ByAppId("70000001")); // now resolved via the remote
    }

    [Fact]
    public void NexusDomains_reflects_a_remote_added_slug()
    {
        Assert.Null(NexusDomains.ByAppId("70000002")); // embedded baseline: unknown

        EffectiveManifest.SetRemote(Remote(new GameManifestEntry
        {
            Id = "remote-nexus-game",
            Name = "Remote Nexus Game",
            Engine = "ue-pak",
            Stores = new StoreIds { SteamAppId = "70000002" },
            NexusDomain = "remotegame",
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.NexusDomains } },
        }));

        Assert.Equal("remotegame", NexusDomains.ByAppId("70000002"));
    }

    [Fact]
    public void PopularGames_reflects_a_remote_featured_game()
    {
        Assert.DoesNotContain(PopularGames.All, g => g.Id == "remote-featured-game"); // baseline

        EffectiveManifest.SetRemote(Remote(new GameManifestEntry
        {
            Id = "remote-featured-game",
            Name = "Remote Featured Game",
            Engine = "bethesda",
            Stores = new StoreIds { SteamAppId = "70000003" },
            ModPath = "Data",
            Featured = 99,
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.PopularGames } },
        }));

        var g = PopularGames.Find("remote-featured-game");
        Assert.NotNull(g);
        Assert.Equal("Remote Featured Game", g!.Name);
        Assert.Equal("bethesda", g.Engine);
        Assert.Equal("Data", g.ModPath);
        Assert.Equal("70000003", g.SteamAppId);
    }
}
