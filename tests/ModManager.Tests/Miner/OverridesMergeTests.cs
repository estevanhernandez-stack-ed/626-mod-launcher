using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class OverridesMergeTests
{
    private static GameManifest Backbone(params (string id, string? steamId, string? engine)[] games) => new()
    {
        Games = games.Select(g => new GameManifestEntry
        {
            Id = g.id, Name = g.id, Engine = g.engine,
            Stores = new StoreIds { SteamAppId = g.steamId },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
        }).ToList(),
    };

    [Fact]
    public void Override_wins_over_mined_fields_on_a_matched_entry()
    {
        var backbone = Backbone(("skyrim", "72850", null));          // mined: no engine
        var overrides = new[] { new OverrideEntry { SteamAppId = "72850", Engine = "bethesda", ModPath = "Data" } };

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single(g => g.Stores.SteamAppId == "72850");
        Assert.Equal("bethesda", e.Engine);
        Assert.Equal("Data", e.ModPath);
        Assert.Contains("curated", e.Provenance.Sources);
        Assert.Equal("curated", e.Provenance.Status);
    }

    [Fact]
    public void Override_replaces_a_value_the_miner_already_set()
    {
        var backbone = Backbone(("x", "1", "custom"));               // mined: wrong/placeholder engine
        var overrides = new[] { new OverrideEntry { SteamAppId = "1", Engine = "bethesda" } };

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single(g => g.Stores.SteamAppId == "1");
        Assert.Equal("bethesda", e.Engine);                          // override wins (not fill-if-empty)
    }

    [Fact]
    public void Override_for_an_unknown_steam_id_adds_a_new_entry()
    {
        var backbone = Backbone(("a", "1", "bethesda"));
        var overrides = new[]
        {
            new OverrideEntry { SteamAppId = "999", Id = "new-game", Name = "New Game", Engine = "ue-pak", ModPath = "Content/Paks/~mods" },
        };

        var result = OverridesMerge.Apply(backbone, overrides);
        var added = result.Games.Single(g => g.Stores.SteamAppId == "999");
        Assert.Equal("new-game", added.Id);
        Assert.Equal("ue-pak", added.Engine);
        Assert.Contains("curated", added.Provenance.Sources);
        Assert.Equal(2, result.Games.Count);
    }

    [Fact]
    public void Unspecified_override_fields_leave_existing_values_intact()
    {
        var backbone = new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "g", Name = "G", Engine = "bethesda", ModPath = "Data",
                    Stores = new StoreIds { SteamAppId = "5" },
                    Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" } },
                },
            },
        };
        var overrides = new[] { new OverrideEntry { SteamAppId = "5", Featured = 3 } }; // only featured

        var e = OverridesMerge.Apply(backbone, overrides).Games.Single();
        Assert.Equal(3, e.Featured);
        Assert.Equal("bethesda", e.Engine);   // untouched
        Assert.Equal("Data", e.ModPath);       // untouched
    }

    [Fact]
    public void An_override_with_no_Steam_id_adds_an_entry_keyed_by_its_slug()
    {
        // The point of the whole change. The launcher resolves a registered game to its manifest entry
        // by slug (Scanner.cs), so an entry added this way is picked up with no launcher change at all.
        var backbone = Backbone(("skyrim", "72850", null));

        var merged = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { Id = "some-ea-game", Name = "Some EA Game", Engine = "custom", ModPath = "Mods" },
        });

        var added = Assert.Single(merged.Games, g => g.Id == "some-ea-game");
        Assert.Equal("custom", added.Engine);
        Assert.Equal("Mods", added.ModPath);
        Assert.Null(added.Stores.SteamAppId);
        Assert.Contains("curated", added.Provenance.Sources);
    }

    [Fact]
    public void An_override_with_no_Steam_id_updates_an_existing_entry_with_the_same_slug()
    {
        var backbone = Backbone(("some-ea-game", null, null));

        var merged = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { Id = "some-ea-game", Engine = "bepinex" },
        });

        Assert.Equal("bepinex", Assert.Single(merged.Games).Engine);   // updated, not duplicated
    }

    [Fact]
    public void The_Steam_id_still_wins_over_the_slug_when_both_could_match()
    {
        // Every one of the 149 existing override files has a Steam id and must keep matching by it.
        // Here the slug points at a DIFFERENT game than the Steam id does; the Steam id is correct.
        var backbone = Backbone(("skyrim", "72850", null), ("some-other-game", "999", null));

        var merged = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { Id = "some-other-game", SteamAppId = "72850", Engine = "bethesda" },
        });

        Assert.Equal("bethesda", merged.Games.Single(g => g.Id == "skyrim").Engine);
        Assert.Null(merged.Games.Single(g => g.Id == "some-other-game").Engine);
    }

    [Fact]
    public void A_Steam_keyed_override_that_does_not_resolve_ADDS_rather_than_taking_a_matching_slug()
    {
        // The regression this shape exists to prevent. The override's slug is an existing game's id,
        // but its Steam id is not in the backbone - so it must add a new entry, never merge into that
        // unrelated game. Before the fix this silently overwrote skyrim's fields.
        var backbone = Backbone(("skyrim", "72850", "bethesda"));

        var merged = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { Id = "skyrim", SteamAppId = "999", Engine = "custom" },
        });

        Assert.Equal("bethesda", merged.Games.Single(g => g.Id == "skyrim").Engine);   // untouched
        Assert.Contains(merged.Games, g => g.Engine == "custom" && g.Id != "skyrim");  // added, suffixed
    }

    [Fact]
    public void Two_slug_only_overrides_deriving_one_slug_do_not_overwrite_each_other_silently()
    {
        // Slug-only overrides have no second key to disambiguate on, so the second one merges into the
        // first. That is why OverridesValidate refuses a duplicate slug before the merge ever runs -
        // this test pins the merge's behaviour so the gate's necessity stays visible.
        var merged = OverridesMerge.Apply(new GameManifest { Games = Array.Empty<GameManifestEntry>() }, new[]
        {
            new OverrideEntry { Id = "same-slug", Name = "First", Engine = "bepinex" },
            new OverrideEntry { Id = "same-slug", Name = "Second", Engine = "custom" },
        });

        Assert.Single(merged.Games);
        Assert.Equal("custom", merged.Games[0].Engine);
    }
}
