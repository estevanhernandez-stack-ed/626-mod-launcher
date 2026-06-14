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

    [Fact]
    public void Remote_null_fields_do_not_blank_embedded_values()
    {
        // The Stardew regression: an auto-mined feed entry (no engine, no rank, no popular-games tag)
        // collides by id with a curated built-in. Feed nulls must NOT erase curated fields, and the
        // provenance union must keep the popular-games tag so the game stays in the quick-pick.
        var embedded = Wrap(new GameManifestEntry
        {
            Id = "stardew-valley", Name = "Stardew Valley", Engine = "smapi", ModPath = "Mods",
            Featured = 4, NexusDomain = "stardewvalley",
            Stores = new StoreIds { SteamAppId = "413150" },
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines", "nexus-domains", "popular-games" }, Status = "curated" },
        });
        var remote = Wrap(new GameManifestEntry
        {
            Id = "stardew-valley", Name = "Stardew Valley", Engine = null, ModPath = "mods",
            Featured = null, NexusDomain = "stardewvalley",
            Stores = new StoreIds { SteamAppId = "413150" },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi", "mo2", "nexus-domains" }, Status = "auto" },
        });

        var sv = EffectiveManifest.Merge(embedded, remote).Games.Single(g => g.Id == "stardew-valley");

        Assert.Equal("smapi", sv.Engine);                        // feed null did not blank the curated engine
        Assert.Equal(4, sv.Featured);                            // nor the quick-pick rank
        Assert.Contains("popular-games", sv.Provenance.Sources); // tag survives -> stays in the picker
        Assert.Equal("mods", sv.ModPath);                        // feed's real value wins (updates, never blanks)
    }

    [Fact]
    public void Provenance_sources_union_without_duplicates_on_collision()
    {
        var embedded = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda",
            Provenance = new ManifestProvenance { Sources = new[] { "popular-games", "known-engines" } },
        });
        var remote = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda",
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines", "nexus-domains", "mo2" } },
        });

        var a = EffectiveManifest.Merge(embedded, remote).Games.Single(g => g.Id == "a");

        Assert.Equal(new[] { "popular-games", "known-engines", "nexus-domains", "mo2" }, a.Provenance.Sources);
    }

    [Fact]
    public void Curated_status_is_not_downgraded_by_an_auto_remote()
    {
        var embedded = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda",
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines" }, Status = "curated" },
        });
        var remote = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda",
            Provenance = new ManifestProvenance { Sources = new[] { "mo2" }, Status = "auto" },
        });

        var a = EffectiveManifest.Merge(embedded, remote).Games.Single(g => g.Id == "a");
        Assert.Equal("curated", a.Provenance.Status);
    }

    [Fact]
    public void Remote_fills_a_field_the_embedded_entry_lacked()
    {
        var embedded = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda", NexusDomain = null,
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
        });
        var remote = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda", NexusDomain = "skyrim",
            Provenance = new ManifestProvenance { Sources = new[] { "nexus-domains" } },
        });

        var a = EffectiveManifest.Merge(embedded, remote).Games.Single(g => g.Id == "a");
        Assert.Equal("skyrim", a.NexusDomain); // feed filled the gap
    }

    [Fact]
    public void Store_ids_merge_field_by_field()
    {
        var embedded = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda",
            Stores = new StoreIds { SteamAppId = "100" },
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
        });
        var remote = Wrap(new GameManifestEntry
        {
            Id = "a", Name = "a", Engine = "bethesda",
            Stores = new StoreIds { SteamAppId = null, GogId = "g1" }, // steam null -> keep embedded; gog -> fill
            Provenance = new ManifestProvenance { Sources = new[] { "nexus-domains" } },
        });

        var a = EffectiveManifest.Merge(embedded, remote).Games.Single(g => g.Id == "a");
        Assert.Equal("100", a.Stores.SteamAppId); // remote null did not blank
        Assert.Equal("g1", a.Stores.GogId);       // remote filled the gap
    }
}
