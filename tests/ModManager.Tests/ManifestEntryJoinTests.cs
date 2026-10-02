using ModManager.Core;
using ModManager.Core.Loaders;
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
        string engine = "custom", string? root = null, string? modPath = null) => new()
    {
        Id = id,
        GameName = id,
        Engine = engine,
        SteamAppId = steamAppId,
        EaContentId = eaContentId,
        GameRoot = root ?? Path.Combine(Path.GetTempPath(), "a30-" + id),
        // Exactly what quick-add writes: the engine preset's path, stored verbatim.
        ModLocations = new[] { new ModLocation("mods", "Mods", modPath ?? EnginePresets.Presets[engine].ModPath) },
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

    // Review on #358: a store id the feed does not know is not a contradiction. The feed correcting an
    // entry's app id, or a user holding another edition's id, must not strip every correction.
    [Fact]
    public void An_unknown_steam_id_still_joins_by_the_own_id()
        => Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(Reg("cyberpunk-2077", "4242424"))?.Id);

    [Fact]
    public void An_unknown_ea_id_still_joins_by_the_own_id()
    {
        FeedWithEaGame();

        Assert.Equal("ea-test-game", ManifestIdLookup.EntryFor(Reg("ea-test-game", eaContentId: "99000002"))?.Id);
    }

    // Review on #358: game ids are not required to be lowercase, so a store match must land on the
    // entry that claims the store id, never on another entry whose id differs only by case.
    [Fact]
    public void A_store_match_lands_on_its_own_entry_not_a_case_variant()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "Case-Game", Name = "A", Engine = "custom", ModPath = "A", Stores = new StoreIds { SteamAppId = "77001" } },
                new GameManifestEntry { Id = "case-game", Name = "B", Engine = "custom", ModPath = "B", Stores = new StoreIds { SteamAppId = "77002" } },
            },
        });

        Assert.Equal("B", ManifestIdLookup.EntryFor(Reg("anything", "77002"))?.ModPath);
        Assert.Equal("B", KnownModPaths.ByAppId("77002"));
    }

    // Review on #358: the add path (KnownModPaths) and the scan path (EntryFor) read one map, so they
    // cannot break a duplicate app id differently.
    [Fact]
    public void The_add_path_and_the_scan_path_pick_the_same_entry_for_an_app_id()
    {
        foreach (var g in EffectiveManifest.Current.Games.Where(g => g.Stores.SteamAppId is not null))
            Assert.Equal(ManifestIdLookup.EntryFor(Reg("x", g.Stores.SteamAppId))?.ModPath is { Length: > 0 } p ? p : null,
                KnownModPaths.ByAppId(g.Stores.SteamAppId));
    }

    // ---- IdsFor, the loader-pin join: same precedence ----------------------------------------

    [Fact]
    public void IdsFor_names_the_store_game_and_not_a_colliding_own_id()
        => Assert.Equal(new[] { "cyberpunk-2077" }, ManifestIdLookup.IdsFor(Reg("elden-ring", CyberpunkAppId)));

    [Fact]
    public void IdsFor_falls_back_to_the_own_id_when_no_store_id_names_a_game()
        => Assert.Equal(new[] { "cyberpunk-2077" }, ManifestIdLookup.IdsFor(Reg("cyberpunk-2077", "4242424")));

    // Review on #358: the loader scan must not treat a registration as a game the mod scan says it is not.
    [Fact]
    public void A_loader_pinned_to_the_colliding_own_id_does_not_reach_the_store_named_game()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Loaders = new[]
            {
                new LoaderManifestEntry
                {
                    Id = "er-only", DisplayName = "ER only", Engine = "fromsoft", GameIds = new[] { "elden-ring" },
                    LauncherExeNames = new[] { "er_only.exe" }, GetUrl = "https://example.com/er", BanSafe = true,
                },
            },
        });

        Assert.Contains(LoaderScan.BanSafeFor(Reg("elden-ring", "1245620", engine: "fromsoft")), l => l.LoaderId == "er-only");
        Assert.DoesNotContain(LoaderScan.BanSafeFor(Reg("elden-ring", CyberpunkAppId, engine: "fromsoft")), l => l.LoaderId == "er-only");
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
    public void GameContext_still_corrects_a_game_whose_steam_id_the_feed_does_not_know()
        => Assert.EndsWith(Path.Combine("archive", "pc", "mod"),
            Scanner.GameContext(Reg("cyberpunk-2077", "4242424")).Locations[0].Abs);

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

    // Review on #358: the seed creates only the folder the scan reads. A customised path the refresh
    // leaves alone is the user's, and a curated folder beside it would be an empty folder never scanned.
    [Fact]
    public void ModFolderSeed_creates_nothing_when_the_scan_reads_a_customised_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "a30-seed-" + Guid.NewGuid().ToString("N"));
        var game = Reg("cyberpunk-2077-2", CyberpunkAppId, root: root, modPath: "my/own/folder");

        Assert.EndsWith("my/own/folder", Scanner.GameContext(game).Locations[0].Abs);
        Assert.Null(ModFolderSeed.PathToCreate(game, exists: p => p == root));
    }

    [Fact]
    public void ModFolderSeed_creates_nothing_for_an_engine_with_no_preset_unless_it_stores_the_curated_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "a30-seed-" + Guid.NewGuid().ToString("N"));
        GameEntry NoPreset(string path) => Reg("cyberpunk-2077", CyberpunkAppId, root: root, modPath: path);

        var stale = NoPreset("mods"); stale.Engine = "not-an-engine";
        var curated = NoPreset("archive/pc/mod"); curated.Engine = "not-an-engine";

        Assert.Null(ModFolderSeed.PathToCreate(stale, exists: p => p == root));
        Assert.NotNull(ModFolderSeed.PathToCreate(curated, exists: p => p == root));
    }
}
