using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class PublishManifestTests
{
    private static GameManifestEntry Entry(string id, string? engine = null, string? nexus = null,
        string? modPath = null, int? featured = null)
        => new()
        {
            Id = id, Name = id, Engine = engine, NexusDomain = nexus, ModPath = modPath, Featured = featured,
            Stores = new StoreIds { SteamAppId = id },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
        };

    private static GameManifest Wrap(params GameManifestEntry[] g) => new() { Games = g };

    [Fact]
    public void Engine_entry_earns_known_engines_tag_and_is_kept()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", engine: "bethesda"))).Games.Single();
        Assert.Contains("known-engines", e.Provenance.Sources);
        Assert.Contains("ludusavi", e.Provenance.Sources); // mining tag preserved
    }

    [Fact]
    public void Nexus_entry_earns_nexus_domains_tag()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", nexus: "skyrim"))).Games.Single();
        Assert.Contains("nexus-domains", e.Provenance.Sources);
    }

    [Fact]
    public void Featured_with_engine_and_modpath_earns_popular_games_tag()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", engine: "bethesda", modPath: "Data", featured: 1))).Games.Single();
        Assert.Contains("popular-games", e.Provenance.Sources);
        Assert.Contains("known-engines", e.Provenance.Sources);
    }

    [Fact]
    public void Featured_without_engine_or_modpath_does_not_earn_popular_games()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", nexus: "x", featured: 1))).Games.Single();
        Assert.DoesNotContain("popular-games", e.Provenance.Sources); // PopularGame projection needs engine+modPath
    }

    [Fact]
    public void Skeletal_entry_is_dropped()
    {
        // name + steamId only — nothing the launcher's facades read.
        var result = PublishManifest.ForPublish(Wrap(Entry("skeletal")));
        Assert.Empty(result.Games);
    }

    [Fact]
    public void Mix_keeps_only_useful_entries()
    {
        var result = PublishManifest.ForPublish(Wrap(
            Entry("keep1", engine: "bethesda"),
            Entry("drop1"),
            Entry("keep2", nexus: "y"),
            Entry("drop2")));
        Assert.Equal(2, result.Games.Count);
        Assert.All(result.Games, g => Assert.True(
            g.Provenance.Sources.Contains("known-engines")
            || g.Provenance.Sources.Contains("nexus-domains")
            || g.Provenance.Sources.Contains("popular-games")));
    }
}
