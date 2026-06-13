using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class EffectiveManifestTests
{
    private static GameManifestEntry Entry(string id, string engine)
        => new()
        {
            Id = id,
            Name = id,
            Engine = engine,
            Stores = new StoreIds { SteamAppId = id },
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
        };

    private static GameManifest Wrap(params GameManifestEntry[] games) => new() { Games = games };

    [Fact]
    public void Null_remote_returns_the_embedded_manifest_unchanged()
    {
        var embedded = Wrap(Entry("a", "bethesda"), Entry("b", "ue-pak"));
        var effective = EffectiveManifest.Merge(embedded, null);

        Assert.Equal(2, effective.Games.Count);
        Assert.Same(embedded, effective); // identity: no copy when there is no remote
    }

    [Fact]
    public void Remote_only_game_is_added()
    {
        var embedded = Wrap(Entry("a", "bethesda"));
        var remote = Wrap(Entry("z", "smapi"));

        var effective = EffectiveManifest.Merge(embedded, remote);

        Assert.Contains(effective.Games, g => g.Id == "a");
        Assert.Contains(effective.Games, g => g.Id == "z");
        Assert.Equal(2, effective.Games.Count);
    }

    [Fact]
    public void Remote_entry_overrides_the_embedded_entry_with_the_same_id()
    {
        var embedded = Wrap(Entry("a", "bethesda"));
        var remote = Wrap(Entry("a", "ue-pak")); // same id, different engine

        var effective = EffectiveManifest.Merge(embedded, remote);

        var a = effective.Games.Single(g => g.Id == "a");
        Assert.Equal("ue-pak", a.Engine);     // remote wins
        Assert.Single(effective.Games);        // no duplicate
    }

    [Fact]
    public void Embedded_entries_not_in_remote_survive()
    {
        var embedded = Wrap(Entry("a", "bethesda"), Entry("b", "ue-pak"));
        var remote = Wrap(Entry("a", "smapi"));

        var effective = EffectiveManifest.Merge(embedded, remote);

        Assert.Equal("smapi", effective.Games.Single(g => g.Id == "a").Engine);
        Assert.Equal("ue-pak", effective.Games.Single(g => g.Id == "b").Engine); // untouched
    }
}
