using ManifestMiner;
using ModManager.Core.Manifest;

namespace ManifestMiner.Tests;

public class GapClassifierTests
{
    private static GameManifestEntry Entry(string id, string? engine, string? nexus) =>
        new() { Id = id, Name = id, Engine = engine, NexusDomain = nexus, Stores = new StoreIds { SteamAppId = id } };

    [Fact]
    public void Classifies_engine_nexus_and_skeletal_into_buckets()
    {
        var games = new[]
        {
            Entry("a", "bethesda", "skyrim"),  // engine-curated
            Entry("b", null, "witcher3"),      // nexus-only (engine-upgrade candidate)
            Entry("c", null, null),            // skeletal (needs full curation)
        };

        var r = GapClassifier.Classify(games);

        Assert.Equal(new[] { "a" }, r.EngineCurated.Select(g => g.Id));
        Assert.Equal(new[] { "b" }, r.NexusOnly.Select(g => g.Id));
        Assert.Equal(new[] { "c" }, r.Skeletal.Select(g => g.Id));
    }

    [Fact]
    public void Engine_present_wins_even_when_nexus_also_set()
    {
        // An entry with both engine + nexusDomain is engine-curated, never double-counted as nexus-only.
        var r = GapClassifier.Classify(new[] { Entry("a", "ue-pak", "lies-of-p") });
        Assert.Single(r.EngineCurated);
        Assert.Empty(r.NexusOnly);
        Assert.Empty(r.Skeletal);
    }
}
