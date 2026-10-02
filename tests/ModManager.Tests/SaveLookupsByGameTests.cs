using ModManager.Core;
using ModManager.Core.Characters;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// The save lookups resolve a registered game, not just a Steam app id.
///
/// <para>The saves panel asked <see cref="SaveDirHints"/>, <see cref="SaveLayoutCatalog"/>,
/// <see cref="SaveSeamCatalog"/> and <see cref="GameSaveTypesCatalog"/> by Steam app id alone. An EA app
/// game is registered with no Steam id on purpose (so Play never routes through <c>steam://</c>), so it
/// could get no curated save folder, no layout and no seam, whatever the feed said. A second store copy
/// (<c>&lt;id&gt;-2</c>) fared no better on its own id. Each now resolves through
/// <see cref="ManifestIdLookup.EntryFor"/>, the join the scan uses since A30.</para>
/// </summary>
[Collection("ManifestState")]
public class SaveLookupsByGameTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null); // never leak state to other tests

    private const string PalworldAppId = "1623730";   // embedded: saveLayout worlds, savePlayerPaths set

    private static GameEntry Game(string id, string? steamAppId = null, string? eaContentId = null, string? engine = "frostbite")
        => new() { Id = id, GameName = id, Engine = engine, SteamAppId = steamAppId, EaContentId = eaContentId };

    // An EA-only game, the shape the feed gives College Football 27: no Steam id, curated save facts.
    private static void FeedWithEaGame() => EffectiveManifest.SetRemote(new GameManifest
    {
        Games = new[]
        {
            new GameManifestEntry
            {
                Id = "ea-save-game", Name = "EA Save Game", Engine = "frostbite",
                Stores = new StoreIds { EaContentId = "99000011" },
                SaveDirHint = "<winDocuments>/EA SPORTS Test/saves",
                SaveLayout = "worlds",
                SavePlayerPaths = new[] { "**/PROFILE-*" },
            },
        },
    });

    [Fact]
    public void An_ea_game_with_no_steam_id_gets_every_curated_save_fact()
    {
        FeedWithEaGame();
        var game = Game("ea-save-game", eaContentId: "99000011");

        Assert.Equal("<winDocuments>/EA SPORTS Test/saves", SaveDirHints.For(game));
        Assert.Equal(SaveLayout.Worlds, SaveLayoutCatalog.For(game));
        Assert.Equal(SaveLayout.Worlds, GameSaveTypesCatalog.Resolve(game).Layout);
        Assert.Equal(new[] { "**/PROFILE-*" }, SaveSeamCatalog.For(game));
        Assert.True(SaveSeamCatalog.CanShareFor(game));
    }

    [Fact]
    public void A_second_ea_copy_gets_them_through_its_content_id()
    {
        FeedWithEaGame();
        var game = Game("ea-save-game-2", eaContentId: "99000011");

        Assert.NotNull(SaveDirHints.For(game));
        Assert.Equal(SaveLayout.Worlds, GameSaveTypesCatalog.Resolve(game).Layout);
        Assert.True(SaveSeamCatalog.CanShareFor(game));
    }

    [Fact]
    public void A_second_steam_copy_gets_its_games_layout_and_seam()
    {
        var game = Game("palworld-2", PalworldAppId, engine: "ue-pak");

        Assert.Equal(SaveLayout.Worlds, GameSaveTypesCatalog.Resolve(game).Layout);
        Assert.True(SaveSeamCatalog.CanShareFor(game));
    }

    // Before this, the only way in was the Steam id, so this is what the EA game got.
    [Fact]
    public void Asking_by_steam_id_alone_still_finds_nothing_for_an_ea_game()
    {
        FeedWithEaGame();

        Assert.Null(SaveDirHints.ByAppId(Game("ea-save-game", eaContentId: "99000011").SteamAppId));
    }

    // A Steam game sees no change. Expectations read straight off the manifest data, not through the
    // lookup under test, so this holds the by-game forms to what the by-app-id maps used to return.
    [Fact]
    public void A_steam_game_gets_its_own_entrys_save_facts()
    {
        foreach (var e in EffectiveManifest.Current.Games.Where(e => e.Stores.SteamAppId is not null))
        {
            var game = Game("registered-" + e.Stores.SteamAppId, e.Stores.SteamAppId, engine: e.Engine);

            Assert.Equal(string.IsNullOrWhiteSpace(e.SaveDirHint) ? null : e.SaveDirHint, SaveDirHints.For(game));
            Assert.Equal(SaveLayoutCatalog.Parse(e.SaveLayout), SaveLayoutCatalog.For(game));
            Assert.Equal(e.SavePlayerPaths ?? Array.Empty<string>(), SaveSeamCatalog.For(game));
        }
        // And the one entry the snapshot curates, named, so the loop above cannot pass by being empty.
        Assert.Equal(SaveLayout.Worlds, SaveLayoutCatalog.For(Game("x", PalworldAppId, engine: "ue-pak")));
    }

    // Review on #359: a save fact points at the user's own data. A slug naming another game's entry
    // ("doom" on Doom Eternal) must not hand this game that entry's save folder, layout or seam when
    // the store id it carries says otherwise. The mod-path join (EntryFor) accepts that case; this one
    // fails closed.
    [Fact]
    public void An_own_id_whose_entry_claims_another_steam_id_gets_no_save_facts()
    {
        var game = Game("palworld", "4242424", engine: "ue-pak");

        Assert.Equal("palworld", ManifestIdLookup.EntryFor(game)?.Id);   // the lenient join still matches
        Assert.Null(ManifestIdLookup.ConfirmedEntryFor(game));
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.For(game));
        Assert.False(SaveSeamCatalog.CanShareFor(game));
    }

    [Fact]
    public void An_own_id_with_no_contradicting_store_id_still_gets_its_save_facts()
    {
        // A GOG or hand-added copy: no store id to contradict the own id.
        Assert.Equal(SaveLayout.Worlds, SaveLayoutCatalog.For(Game("palworld", engine: "ue-pak")));
    }

    [Fact]
    public void An_own_id_whose_entry_claims_another_ea_id_gets_no_save_facts()
    {
        FeedWithEaGame();

        Assert.Null(SaveDirHints.For(Game("ea-save-game", eaContentId: "99000099")));
    }

    [Fact]
    public void An_unknown_game_gets_the_floor_not_a_guess()
    {
        var game = Game("not-in-any-manifest", eaContentId: "12345");

        Assert.Null(SaveDirHints.For(game));
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.For(game));
        Assert.Empty(SaveSeamCatalog.For(game));
        Assert.False(SaveSeamCatalog.CanShareFor(game));
        Assert.Null(SaveDirHints.For(null));
    }

    [Fact]
    public void A_curated_save_fact_the_feed_adds_reaches_a_registered_ea_game_without_a_restart()
    {
        var game = Game("ea-save-game", eaContentId: "99000011");
        Assert.Equal(SaveLayout.TypedFiles, GameSaveTypesCatalog.Resolve(game).Layout);

        FeedWithEaGame();
        Assert.Equal(SaveLayout.Worlds, GameSaveTypesCatalog.Resolve(game).Layout);
    }

    // Review on #359: the Cyberpunk reader is keyed on the Steam app id, carried by the registration
    // or by the entry it resolves to, never on a manifest id the feed can rename.
    [Fact]
    public void The_cyberpunk_reader_applies_to_every_copy_that_names_the_game()
    {
        Assert.True(CyberpunkCharacters.AppliesTo(Game("cyberpunk-2077", "1091500", engine: "custom")));
        Assert.True(CyberpunkCharacters.AppliesTo(Game("cyberpunk-2077-2", "1091500", engine: "custom")));
        Assert.True(CyberpunkCharacters.AppliesTo(Game("cyberpunk-2077", engine: "custom")));      // GOG: no Steam id
        Assert.False(CyberpunkCharacters.AppliesTo(Game("elden-ring", "1245620", engine: "fromsoft")));
        Assert.False(CyberpunkCharacters.AppliesTo(null));
    }

    [Fact]
    public void The_cyberpunk_reader_survives_a_feed_that_renames_the_entry()
    {
        // The feed's id wins a Steam-id collision (EffectiveManifest.Merge), as skyrim-se did.
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "cyberpunk-2077-ultimate", Name = "Cyberpunk", Engine = "custom", Stores = new StoreIds { SteamAppId = "1091500" } } },
        });

        Assert.DoesNotContain(EffectiveManifest.Current.Games, g => g.Id == "cyberpunk-2077");
        Assert.True(CyberpunkCharacters.AppliesTo(Game("cyberpunk-2077-2", "1091500", engine: "custom")));
    }

    // Review on #359: Merge folded a feed rename by Steam id only. An EA-only game renamed by the feed
    // left two entries on one content id, and every by-store lookup took the snapshot's stale one.
    [Fact]
    public void A_feed_rename_of_an_ea_game_folds_into_one_entry_carrying_the_feeds_facts()
    {
        var embedded = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "cfb-27", Name = "Old", Engine = "frostbite", Stores = new StoreIds { EaContentId = "99000021" } } },
        };
        var remote = new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "college-football-27", Name = "New", Engine = "frostbite", Stores = new StoreIds { EaContentId = "99000021" }, SaveLayout = "worlds" } },
        };

        var merged = EffectiveManifest.Merge(embedded, remote);

        var only = Assert.Single(merged.Games);
        Assert.Equal("college-football-27", only.Id);
        Assert.Equal("worlds", only.SaveLayout);
    }
}
