using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// A feed entry, once tagged by the miner's publish transform, must actually reach the facades.
[Collection("ManifestState")]
public class FeedConsumptionTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    [Fact]
    public void Facades_consume_a_published_feed_entry()
    {
        Assert.Null(KnownEngines.ByAppId("900001")); // baseline: unknown

        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "feed-game", Name = "Feed Game", Engine = "bethesda",
                    Stores = new StoreIds { SteamAppId = "900001" }, NexusDomain = "feedgame",
                    // the tags the miner's PublishManifest.ForPublish stamps:
                    Provenance = new ManifestProvenance
                    {
                        Sources = new[] { "ludusavi", "mo2", "known-engines", "nexus-domains" },
                        Status = "curated",
                    },
                },
            },
        });

        Assert.Equal("bethesda", KnownEngines.ByAppId("900001"));   // consumed via known-engines tag
        Assert.Equal("feedgame", NexusDomains.ByAppId("900001"));   // consumed via nexus-domains tag
    }

    [Fact]
    public void Untagged_feed_entry_is_not_consumed()
    {
        // Guards the failure mode we hit: mining-only tags must NOT reach the facades
        // (the publish transform is what adds the functional tags).
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "raw", Name = "Raw", Engine = "bethesda",
                    Stores = new StoreIds { SteamAppId = "900002" },
                    Provenance = new ManifestProvenance { Sources = new[] { "ludusavi", "mo2" } },
                },
            },
        });
        Assert.Null(KnownEngines.ByAppId("900002")); // no functional tag -> not consumed
    }
}
