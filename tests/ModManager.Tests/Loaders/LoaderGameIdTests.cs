using System.IO;
using ModManager.Core;
using ModManager.Core.Loaders;
using ModManager.Core.Manifest;
using static ModManager.Tests.Loaders.LoaderTestKit;

namespace ModManager.Tests.Loaders;

// A loader could only be pinned to one game by Steam app id, the same Steam-only lookup the ban-risk
// work removed in September. An EA app game is registered with no Steam id on purpose, so a loader
// meant for it could not be written at all, and a loader pinned to a game sold on both stores showed
// for the Steam copy only. `gameIds` pins by manifest id, and a registration is resolved to its
// manifest ids through every identity it carries (ManifestIdLookup.IdsFor), not by its raw id.
[Collection("ManifestState")]
public class LoaderGameIdTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null); // never leak state to other tests

    private static LoaderManifestEntry Loader(string id, string engine, string exe,
        string? steamAppId = null, string[]? gameIds = null)
        => new()
        {
            Id = id, DisplayName = id, Engine = engine, SteamAppId = steamAppId, GameIds = gameIds,
            LauncherExeNames = new[] { exe }, GetUrl = "https://example.com/" + id, Author = "someone",
            BanSafe = true,
        };

    private static GameManifestEntry ManifestGame(string id, string? steamAppId = null, string? eaContentId = null)
        => new()
        {
            Id = id, Name = id, Engine = "frostbite",
            Stores = new StoreIds { SteamAppId = steamAppId, EaContentId = eaContentId },
        };

    // Through the real gate, as a fetched feed would arrive.
    private static void Feed(GameManifestEntry[] games, params LoaderManifestEntry[] loaders)
    {
        var validated = ManifestValidator.Validate(
            new GameManifest { Games = games, Loaders = loaders }, EnginePresets.Presets.Keys.ToHashSet());
        Assert.Empty(validated.RejectedLoaders);
        EffectiveManifest.SetRemote(validated.Manifest);
    }

    private static readonly GameManifestEntry[] NoGames = Array.Empty<GameManifestEntry>();

    [Fact]
    public void A_loader_pinned_by_game_id_reaches_an_ea_game_with_no_steam_id()
    {
        var dir = TempPlayFolder("roster_tool.exe");
        try
        {
            Feed(NoGames, Loader("madden-roster-tool", "frostbite", "roster_tool.exe", gameIds: new[] { "madden-nfl-27" }));
            var madden = Game("madden-nfl-27", "frostbite");   // as EaGameImport registers it: no Steam id

            Assert.Equal("madden-roster-tool", Assert.Single(LoaderScan.Detect(dir, madden)).Loader.LoaderId);
            Assert.Contains(LoaderScan.BanSafeFor(madden), l => l.LoaderId == "madden-roster-tool");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_loader_pinned_by_game_id_does_not_reach_another_game_on_the_same_engine()
    {
        var dir = TempPlayFolder("roster_tool.exe");
        try
        {
            Feed(NoGames, Loader("madden-roster-tool", "frostbite", "roster_tool.exe", gameIds: new[] { "madden-nfl-27" }));

            Assert.Empty(LoaderScan.Detect(dir, Game("college-football-27", "frostbite")));
            Assert.Empty(LoaderScan.BanSafeFor(Game("college-football-27", "frostbite")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // Review on #356: the registry never holds two registrations with one id. The second store copy of
    // a game is renamed by EnginePresets.UniqueId, so this builds them the way the registry does, and
    // the EA copy is reached through its EA content id rather than its "-2" id.
    [Fact]
    public void A_loader_pinned_by_game_id_reaches_both_store_copies_as_the_registry_holds_them()
    {
        var dir = TempPlayFolder("tool.exe");
        try
        {
            Feed(new[] { ManifestGame("some-game", steamAppId: "123", eaContentId: "Origin.OFR.50.0001") },
                Loader("both-stores-tool", "frostbite", "tool.exe", gameIds: new[] { "some-game" }));

            var steamCopy = Game("some-game", "frostbite", steamAppId: "123");
            var eaId = EnginePresets.UniqueId("some-game", new[] { steamCopy.Id });
            Assert.Equal("some-game-2", eaId);   // what the registry actually does to the second copy
            var eaCopy = Game(eaId, "frostbite", eaContentId: "Origin.OFR.50.0001");

            Assert.Single(LoaderScan.Detect(dir, steamCopy));
            Assert.Single(LoaderScan.Detect(dir, eaCopy));
        }
        finally { Directory.Delete(dir, true); }
    }

    // Review on #356: a registration added before ManifestIdLookup existed, or while the feed was
    // unreachable, carries a slug of its display name. Its Steam id still names the game.
    [Fact]
    public void A_registration_with_a_legacy_name_slug_is_reached_through_its_steam_id()
    {
        var dir = TempPlayFolder("tool.exe");
        try
        {
            Feed(new[] { ManifestGame("minecraft", steamAppId: "1672970") },
                Loader("game-pinned", "frostbite", "tool.exe", gameIds: new[] { "minecraft" }));

            Assert.Single(LoaderScan.Detect(dir, Game("minecraft-java-edition", "frostbite", steamAppId: "1672970")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // Review on #356: BanRiskCatalog matches ids case-insensitively, so this does too. Otherwise one
    // registration could get the ban-risk warning while missing the safe loader pinned to it.
    [Fact]
    public void A_game_id_pin_matches_regardless_of_the_registrations_casing()
    {
        var dir = TempPlayFolder("roster_tool.exe");
        try
        {
            Feed(NoGames, Loader("madden-roster-tool", "frostbite", "roster_tool.exe", gameIds: new[] { "madden-nfl-27" }));

            Assert.Single(LoaderScan.Detect(dir, Game("Madden-NFL-27", "frostbite")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // Stated so nobody mistakes it for a bug: a Steam-id pin is still Steam-only. That is why gameIds
    // exists, and a curator who means "this game on any store" pins by game id.
    [Fact]
    public void A_loader_pinned_only_by_steam_id_still_misses_the_ea_copy()
    {
        var dir = TempPlayFolder("tool.exe");
        try
        {
            Feed(NoGames, Loader("steam-only-tool", "frostbite", "tool.exe", steamAppId: "123"));

            Assert.Single(LoaderScan.Detect(dir, Game("some-game", "frostbite", steamAppId: "123")));
            Assert.Empty(LoaderScan.Detect(dir, Game("some-game-2", "frostbite", eaContentId: "Origin.OFR.50.0001")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // Either pin is enough when a loader carries both.
    [Fact]
    public void A_loader_with_both_pins_matches_on_either()
    {
        var dir = TempPlayFolder("tool.exe");
        try
        {
            Feed(NoGames, Loader("dual-pin", "frostbite", "tool.exe", steamAppId: "123", gameIds: new[] { "ea-only-game" }));

            Assert.Single(LoaderScan.Detect(dir, Game("steam-copy", "frostbite", steamAppId: "123")));
            Assert.Single(LoaderScan.Detect(dir, Game("ea-only-game", "frostbite")));
            Assert.Empty(LoaderScan.Detect(dir, Game("unrelated", "frostbite", steamAppId: "999")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void An_engine_wide_loader_reaches_an_ea_game_too()
    {
        var dir = TempPlayFolder("frostbite_loader.exe");
        try
        {
            Feed(NoGames, Loader("frostbite-wide", "frostbite", "frostbite_loader.exe"));

            Assert.Single(LoaderScan.Detect(dir, Game("madden-nfl-27", "frostbite")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // The engine still has to match: a game-id pin narrows a loader, it never moves it to another engine.
    [Fact]
    public void A_game_id_pin_does_not_override_the_engine()
    {
        var dir = TempPlayFolder("roster_tool.exe");
        try
        {
            Feed(NoGames, Loader("madden-roster-tool", "frostbite", "roster_tool.exe", gameIds: new[] { "madden-nfl-27" }));

            Assert.Empty(LoaderScan.Detect(dir, Game("madden-nfl-27", "custom")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // A game with no engine has no mod lane, so no loader applies, engine-wide or pinned.
    [Fact]
    public void A_game_with_no_engine_gets_no_loaders()
        => Assert.Empty(LoaderScan.BanSafeFor(Game("elden-ring", engine: null, steamAppId: "1245620")));

    // Review on #356: pins fail closed. A loader carrying a field this binary does not know may be
    // scoped by it, so it is skipped, never offered as if engine-wide.
    [Fact]
    public void A_loader_with_a_field_this_binary_does_not_know_is_skipped_not_treated_as_unpinned()
    {
        var json = """
            { "games": [], "loaders": [ {
              "id": "future-pinned", "displayName": "Future", "engine": "frostbite",
              "eaContentIds": ["Origin.OFR.50.0001"],
              "launcherExeNames": ["tool.exe"], "getUrl": "https://example.com/f", "banSafe": true } ] }
            """;
        var parsed = System.Text.Json.JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options)!;

        var result = ManifestValidator.Validate(parsed, EnginePresets.Presets.Keys.ToHashSet());

        Assert.Empty(result.Manifest.Loaders);
        Assert.Equal(new[] { "future-pinned" }, result.SkippedLoaders);
    }
}
