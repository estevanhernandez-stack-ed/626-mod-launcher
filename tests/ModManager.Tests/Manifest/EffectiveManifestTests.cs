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

public class BanRiskMergeTests
{
    private static GameManifest One(string id, string? banRisk) => new()
    {
        Games = new[] { new GameManifestEntry { Id = id, Name = id, BanRisk = banRisk } },
    };

    [Fact]
    public void Remote_null_cannot_blank_a_curated_high()
    {
        var merged = EffectiveManifest.Merge(One("g", "high"), One("g", null));
        Assert.Equal("high", merged.Games[0].BanRisk);
    }

    [Fact]
    public void Remote_can_raise_risk()
    {
        var merged = EffectiveManifest.Merge(One("g", "low"), One("g", "high"));
        Assert.Equal("high", merged.Games[0].BanRisk);
    }

    [Fact]
    public void Remote_lower_cannot_downgrade_curated_high()
    {
        var merged = EffectiveManifest.Merge(One("g", "high"), One("g", "low"));
        Assert.Equal("high", merged.Games[0].BanRisk);
    }
}

/// <summary>
/// The merge must not quietly forget a field.
///
/// <para><c>MergeEntry</c> builds <c>embedded with { … }</c> and names each field by hand, so a field
/// added to the schema and not added to that list is silently dropped for every id the feed and the
/// embedded snapshot both carry — and it fails <b>only</b> on those ids, which are the important ones,
/// because being important is what puts a game in the snapshot.</para>
///
/// <para>That is exactly what happened to <c>safeRoute</c> / <c>safeRouteHint</c>: shipped in the
/// schema, wired through the miner, never added to the merge. Three games lost it — elden-ring,
/// dark-souls-iii, palworld — and nothing noticed because no UI consumed it yet.</para>
///
/// <para>This test walks the record by reflection rather than listing fields, so a NEW field is
/// covered the moment it is declared. That is the whole point: a hand-written list is the thing that
/// failed.</para>
/// </summary>
public class ManifestMergeCompletenessTests
{
    // Composite fields with their own merge rules and their own tests: unioned, not overwritten.
    private static readonly HashSet<string> Composite = new() { "Id", "Stores", "Provenance" };

    [Fact]
    public void Every_optional_field_the_schema_declares_survives_the_merge()
    {
        // The remote entry is BUILT by reflection, not hand-written. An earlier version of this test
        // walked the properties but only asserted the ones a hand-written fixture happened to set -
        // so a newly declared field was silently skipped, which is the exact hand-maintained-list
        // failure the test exists to prevent. It passed while SaveLayout was unmerged.
        var embedded = new GameManifestEntry { Id = "g" };
        var remote = new GameManifestEntry { Id = "g" };
        var wanted = new Dictionary<string, object?>();

        foreach (var prop in typeof(GameManifestEntry).GetProperties())
        {
            if (Composite.Contains(prop.Name)) continue;
            var value = SampleFor(prop.PropertyType, prop.Name);
            if (value is null) continue;                       // a type we cannot synthesise
            wanted[prop.Name] = value;
            remote = (GameManifestEntry)prop.DeclaringType!
                .GetMethod("<Clone>$", System.Reflection.BindingFlags.Instance
                                     | System.Reflection.BindingFlags.NonPublic
                                     | System.Reflection.BindingFlags.Public)!
                .Invoke(remote, null)!;
            prop.SetValue(remote, value);
        }

        // Nothing was synthesisable? Then the test is asserting nothing and must say so.
        Assert.NotEmpty(wanted);

        var merged = EffectiveManifest
            .Merge(new GameManifest { Games = new[] { embedded } },
                   new GameManifest { Games = new[] { remote } })
            .Games.Single(g => g.Id == "g");

        var missed = typeof(GameManifestEntry).GetProperties()
            .Where(p => wanted.ContainsKey(p.Name))
            .Where(p => !Equals(FlattenForCompare(wanted[p.Name]), FlattenForCompare(p.GetValue(merged))))
            .Select(p => p.Name)
            .ToList();

        Assert.True(missed.Count == 0,
            "MergeEntry drops these fields the schema declares: " + string.Join(", ", missed) +
            ". Add them to the `embedded with { … }` list in EffectiveManifest.MergeEntry.");
    }

    /// <summary>A non-default value for a property type, so the merge has something to lose.</summary>
    private static object? SampleFor(Type t, string name)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(string)) return name == "BanRisk" ? "high" : "remote-" + name;
        if (u == typeof(int)) return 4242;
        if (u == typeof(bool)) return true;
        if (typeof(IReadOnlyList<string>).IsAssignableFrom(t)) return new[] { "remote-" + name };
        return null;
    }

    private static object? FlattenForCompare(object? v)
        => v is IEnumerable<string> seq ? string.Join("|", seq) : v;
}
