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
}
