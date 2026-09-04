using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// The Add Game quick-pick catalogue.
///
/// <para>It used to project only entries carrying the legacy <c>popular-games</c> provenance tag — 18
/// of 156 — which made a newly curated game invisible in the one surface built for finding a curated
/// game. For a Steam title that went unnoticed, because Steam detection finds it anyway. For a game
/// sold anywhere else the picker is the ONLY route, so "curated but unfindable" was the whole of the
/// user's experience of it.</para>
///
/// <para>It now offers every entry the projection can actually represent: one with an engine and a mod
/// path. An entry missing either cannot become a <see cref="PopularGame"/> without inventing values,
/// and inventing a mod path is how files land somewhere a loader never looks.</para>
/// </summary>
// One test injects a remote fixture via EffectiveManifest.SetRemote to prove the widening as a rule
// rather than a count against ambient shipped data — same reason FacadeRemoteWiringTests is in this
// collection. DisableParallelization on "ManifestState" keeps that test from racing any other test
// that mutates the same process-global static.
[Collection("ManifestState")]
public class PopularGamesTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    [Fact]
    public void Every_entry_with_an_engine_and_a_mod_path_is_offered()
    {
        var offered = PopularGames.All.Select(g => g.Id).ToHashSet();
        var representable = EffectiveManifest.Current.Games
            .Where(g => g.Engine is not null && g.ModPath is not null)
            .Select(g => g.Id);

        Assert.All(representable, id => Assert.Contains(id, offered));
    }

    [Fact]
    public void An_entry_with_no_engine_or_no_mod_path_is_left_out()
    {
        // Not a filter for tidiness: PopularGame's Engine and ModPath are non-nullable, so an entry
        // missing either could only be projected by inventing a value. Inventing a mod path is how
        // files land somewhere the loader never looks.
        var offered = PopularGames.All.Select(g => g.Id).ToHashSet();

        foreach (var g in EffectiveManifest.Current.Games.Where(g => g.Engine is null || g.ModPath is null))
            Assert.DoesNotContain(g.Id, offered);
    }

    [Fact]
    public void An_entry_without_the_legacy_tag_is_still_offered()
    {
        // The tag reproduced a hand-written array's contents. Gating membership on it left a newly
        // curated game invisible in the one surface built for finding curated games. Prove the
        // widening as a RULE — an entry with an engine and a mod path but no popular-games tag is
        // still offered — rather than a count against whatever manifest happens to be ambient, which
        // only holds if the shipped snapshot happens to contain such an entry.
        Assert.DoesNotContain(PopularGames.All, g => g.Id == "untagged-but-representable"); // baseline

        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "untagged-but-representable",
                    Name = "Untagged But Representable",
                    Engine = "bethesda",
                    ModPath = "Data",
                    Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
                },
            },
        });

        Assert.Contains(PopularGames.All, g => g.Id == "untagged-but-representable");
    }

    [Fact]
    public void Featured_games_come_first_in_their_stated_order()
    {
        var featured = PopularGames.All
            .Select((g, i) => (g.Id, i))
            .Join(EffectiveManifest.Current.Games.Where(m => m.Featured is not null),
                  x => x.Id, m => m.Id, (x, m) => (x.i, rank: m.Featured!.Value))
            .OrderBy(x => x.rank)
            .ToList();

        // Their positions in the list must ascend with their featured rank, and all must precede
        // anything unfeatured.
        Assert.Equal(featured.OrderBy(x => x.i).Select(x => x.i), featured.Select(x => x.i));
        var firstUnfeatured = PopularGames.All
            .Select((g, i) => (g.Id, i))
            .Where(x => EffectiveManifest.Current.Games.First(m => m.Id == x.Id).Featured is null)
            .Select(x => x.i)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        Assert.All(featured, x => Assert.True(x.i < firstUnfeatured));
    }

    [Fact]
    public void A_game_with_no_Steam_id_is_offered_and_carries_a_null_id()
    {
        // Minecraft is the case this exists for: curated, moddable, and on no store the projection
        // used to be able to express. The old code forced g.Stores.SteamAppId! into a non-nullable
        // field, so the one game that proves non-Steam support was the one it could not represent.
        var offered = PopularGames.All.ToList();

        Assert.All(offered.Where(g => g.SteamAppId is not null),
                   g => Assert.False(string.IsNullOrWhiteSpace(g.SteamAppId)));
        // Any entry curated without a Steam id must still be offered, with a null id rather than "".
        foreach (var m in EffectiveManifest.Current.Games
                     .Where(m => m.Engine is not null && m.ModPath is not null && m.Stores.SteamAppId is null))
            Assert.Null(offered.Single(g => g.Id == m.Id).SteamAppId);
    }

    [Fact]
    public void Find_still_resolves_by_id_and_returns_null_for_an_unknown_one()
    {
        var any = PopularGames.All[0];

        Assert.Equal(any.Id, PopularGames.Find(any.Id)!.Id);
        Assert.Null(PopularGames.Find("not-a-real-game"));
        Assert.Null(PopularGames.Find(null));
    }
}
