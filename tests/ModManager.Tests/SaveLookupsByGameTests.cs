using ModManager.Core;
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
        var game = Game("ea-save-game", eaContentId: "99000011");

        Assert.Null(SaveDirHints.ByAppId(game.SteamAppId));
        Assert.Equal(SaveLayout.TypedFiles, GameSaveTypesCatalog.Resolve(game.Engine, game.SteamAppId).Layout);
        Assert.False(SaveSeamCatalog.CanShare(game.SteamAppId));
    }

    // A Steam game must see no change: the by-game answer is the by-app-id answer for every entry.
    [Fact]
    public void A_steam_game_gets_the_same_answer_by_game_as_by_app_id()
    {
        foreach (var e in EffectiveManifest.Current.Games.Where(e => e.Stores.SteamAppId is not null))
        {
            var appId = e.Stores.SteamAppId!;
            var game = Game("registered-" + appId, appId, engine: e.Engine);

            Assert.Equal(SaveDirHints.ByAppId(appId), SaveDirHints.For(game));
            Assert.Equal(SaveLayoutCatalog.ByAppId(appId), SaveLayoutCatalog.For(game));
            Assert.Equal(SaveSeamCatalog.ByAppId(appId), SaveSeamCatalog.For(game));
            Assert.Equal(GameSaveTypesCatalog.Resolve(e.Engine, appId).Layout, GameSaveTypesCatalog.Resolve(game).Layout);
            Assert.Equal(GameSaveTypesCatalog.Resolve(e.Engine, appId).SaveTypes, GameSaveTypesCatalog.Resolve(game).SaveTypes);
        }
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

    // The Cyberpunk character reader dispatches on the manifest id. Pinned here, where it can run:
    // every store's copy of the game resolves to that id.
    [Fact]
    public void Every_copy_of_cyberpunk_resolves_to_the_id_the_character_reader_dispatches_on()
    {
        Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Game("cyberpunk-2077", "1091500", engine: "custom"))?.Id);
        Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Game("cyberpunk-2077-2", "1091500", engine: "custom"))?.Id);
        Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Game("cyberpunk-2077", engine: "custom"))?.Id);   // GOG: no Steam id
    }
}
