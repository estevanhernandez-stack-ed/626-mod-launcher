using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// A30. A registration joins to its manifest entry so a correction (file extensions, grouping, mod
/// path) reaches a game the user already added. The join was the raw id, so a second store copy
/// (<c>&lt;id&gt;-2</c>, from <c>EnginePresets.UniqueId</c>) and an older registration carrying a slug of
/// its display name never got one. It now goes through store identity first, the same way ban risk
/// (#357) and loader pins (#356) do.
/// </summary>
[Collection("ManifestState")]
public class ManifestEntryJoinTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null); // never leak state to other tests

    // cyberpunk-2077 is in the embedded snapshot: Steam 1091500, modPath archive/pc/mod, engine custom.
    private const string CyberpunkAppId = "1091500";

    private static GameEntry Reg(string id, string? steamAppId = null, string? eaContentId = null,
        string engine = "custom", string? root = null) => new()
    {
        Id = id,
        GameName = id,
        Engine = engine,
        SteamAppId = steamAppId,
        EaContentId = eaContentId,
        GameRoot = root ?? Path.Combine(Path.GetTempPath(), "a30-" + id),
        // Exactly what quick-add writes: the engine preset's path, stored verbatim.
        ModLocations = new[] { new ModLocation("mods", "Mods", EnginePresets.Presets[engine].ModPath) },
    };

    // An EA-only game, as the feed carries one: no Steam id, a curated mod folder.
    private static void FeedWithEaGame() => EffectiveManifest.SetRemote(new GameManifest
    {
        Games = new[]
        {
            new GameManifestEntry
            {
                Id = "ea-test-game", Name = "EA Test Game", Engine = "custom", ModPath = "Data/Mods",
                Stores = new StoreIds { EaContentId = "99000001" },
            },
        },
    });

    // ---- the resolver ----------------------------------------------------------------------

    [Fact]
    public void The_own_id_still_names_its_entry()
        => Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Reg("cyberpunk-2077", CyberpunkAppId))?.Id);

    [Fact]
    public void A_second_store_copy_is_named_by_its_steam_id()
        => Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Reg("cyberpunk-2077-2", CyberpunkAppId))?.Id);

    [Fact]
    public void A_display_name_slug_is_named_by_its_steam_id()
        => Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Reg("cyberpunk-2077-ultimate", CyberpunkAppId))?.Id);

    [Fact]
    public void An_ea_copy_is_named_by_its_content_id()
    {
        FeedWithEaGame();

        Assert.Equal("ea-test-game", ManifestIdLookup.EntryFor(Reg("ea-test-game-2", eaContentId: "99000001"))?.Id);
    }

    [Fact]
    public void The_own_id_matches_regardless_of_case()
        => Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Reg("Cyberpunk-2077"))?.Id);

    // A slug can collide with a different game's manifest id. The store id names exactly one game.
    [Fact]
    public void The_store_id_outranks_an_own_id_naming_another_game()
        => Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Reg("elden-ring", CyberpunkAppId))?.Id);

    // The own id names Cyberpunk, but the registration is a Steam game Cyberpunk is not. Applying
    // Cyberpunk's mod folder would point the scan and intake somewhere this game does not use.
    [Fact]
    public void An_own_id_whose_entry_claims_a_different_steam_id_is_no_match()
        => Assert.Null(ManifestIdLookup.EntryFor(Reg("cyberpunk-2077", "4242424")));

    [Fact]
    public void An_own_id_whose_entry_claims_a_different_ea_id_is_no_match()
    {
        FeedWithEaGame();

        Assert.Null(ManifestIdLookup.EntryFor(Reg("ea-test-game", eaContentId: "99000002")));
    }

    [Fact]
    public void An_unknown_game_and_a_null_game_are_no_match()
    {
        Assert.Null(ManifestIdLookup.EntryFor(Reg("not-in-any-manifest", "4242424")));
        Assert.Null(ManifestIdLookup.EntryFor(null));
    }

    [Fact]
    public void A_game_the_feed_adds_is_joined_without_a_restart()
    {
        var game = Reg("ea-test-game-2", eaContentId: "99000001");
        Assert.Null(ManifestIdLookup.EntryFor(game));

        FeedWithEaGame();
        Assert.Equal("ea-test-game", ManifestIdLookup.EntryFor(game)?.Id);

        EffectiveManifest.SetRemote(null);
        Assert.Null(ManifestIdLookup.EntryFor(game));
    }

    // ---- end to end: the scan and the folder seed agree ----------------------------------------

    [Fact]
    public void GameContext_repairs_the_mod_path_of_a_second_steam_copy()
    {
        var game = Reg("cyberpunk-2077-2", CyberpunkAppId);

        Assert.EndsWith(Path.Combine("archive", "pc", "mod"), Scanner.GameContext(game).Locations[0].Abs);
        Assert.Equal("mods", game.ModLocations[0].Path);   // read-only: the stored entry is untouched
    }

    [Fact]
    public void GameContext_repairs_the_mod_path_of_a_second_ea_copy()
    {
        FeedWithEaGame();

        var ctx = Scanner.GameContext(Reg("ea-test-game-2", eaContentId: "99000001"));

        Assert.EndsWith(Path.Combine("Data", "Mods"), ctx.Locations[0].Abs);
    }

    [Fact]
    public void GameContext_takes_no_correction_from_a_contradicted_entry()
        => Assert.EndsWith("mods", Scanner.GameContext(Reg("cyberpunk-2077", "4242424")).Locations[0].Abs);

    [Fact]
    public void GameContext_takes_the_store_named_games_correction_over_the_own_id()
    {
        // Elden Ring curates no mod path; Cyberpunk does. The Steam id says this is Cyberpunk.
        Assert.EndsWith(Path.Combine("archive", "pc", "mod"),
            Scanner.GameContext(Reg("elden-ring", CyberpunkAppId)).Locations[0].Abs);
        Assert.Null(EffectiveManifest.Current.Games.Single(g => g.Id == "elden-ring").ModPath);
    }

    [Fact]
    public void ModFolderSeed_creates_the_curated_folder_for_a_second_steam_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "a30-seed-" + Guid.NewGuid().ToString("N"));

        var path = ModFolderSeed.PathToCreate(Reg("cyberpunk-2077-2", CyberpunkAppId, root: root), exists: p => p == root);

        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("archive", "pc", "mod"), path);
    }

    [Fact]
    public void ModFolderSeed_creates_nothing_for_a_contradicted_entry()
    {
        var root = Path.Combine(Path.GetTempPath(), "a30-seed-" + Guid.NewGuid().ToString("N"));

        Assert.Null(ModFolderSeed.PathToCreate(Reg("cyberpunk-2077", "4242424", root: root), exists: p => p == root));
    }
}
