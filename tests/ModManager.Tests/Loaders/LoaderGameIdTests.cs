using System.IO;
using ModManager.Core;
using ModManager.Core.Loaders;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Loaders;

// A loader could only be pinned to one game by Steam app id, the same Steam-only lookup the ban-risk
// work removed in September. An EA app game is registered with no Steam id on purpose, so a loader
// meant for it could not be written at all, and a loader pinned to a game sold on both stores showed
// for the Steam copy only. `gameIds` pins by manifest id, which every registration carries.
[Collection("ManifestState")]
public class LoaderGameIdTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null); // never leak state to other tests

    private static string TempPlayFolder(params string[] files)
    {
        var d = Path.Combine(Path.GetTempPath(), "mm-loader-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        foreach (var f in files) File.WriteAllText(Path.Combine(d, f), "x");
        return d;
    }

    private static GameEntry Game(string id, string engine, string? steamAppId = null)
        => new() { Id = id, GameName = id, Engine = engine, SteamAppId = steamAppId };

    private static LoaderManifestEntry Loader(string id, string engine, string exe,
        string? steamAppId = null, string[]? gameIds = null)
        => new()
        {
            Id = id, DisplayName = id, Engine = engine, SteamAppId = steamAppId, GameIds = gameIds,
            LauncherExeNames = new[] { exe }, GetUrl = "https://example.com/" + id, Author = "someone",
            BanSafe = true,
        };

    // Through the real gate, as a fetched feed would arrive.
    private static void Feed(params LoaderManifestEntry[] loaders)
    {
        var validated = ManifestValidator.Validate(
            new GameManifest { Loaders = loaders }, EnginePresets.Presets.Keys.ToHashSet());
        Assert.Empty(validated.RejectedLoaders);
        EffectiveManifest.SetRemote(validated.Manifest);
    }

    [Fact]
    public void A_loader_pinned_by_game_id_reaches_an_ea_game_with_no_steam_id()
    {
        var dir = TempPlayFolder("roster_tool.exe");
        try
        {
            Feed(Loader("madden-roster-tool", "frostbite", "roster_tool.exe", gameIds: new[] { "madden-nfl-27" }));
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
            Feed(Loader("madden-roster-tool", "frostbite", "roster_tool.exe", gameIds: new[] { "madden-nfl-27" }));

            Assert.Empty(LoaderScan.Detect(dir, Game("college-football-27", "frostbite")));
            Assert.Empty(LoaderScan.BanSafeFor(Game("college-football-27", "frostbite")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // The multi-store case: one manifest id, two registrations, only one of them with a Steam id.
    [Fact]
    public void A_loader_pinned_by_game_id_reaches_both_store_copies_of_one_game()
    {
        var dir = TempPlayFolder("tool.exe");
        try
        {
            Feed(Loader("both-stores-tool", "frostbite", "tool.exe", gameIds: new[] { "some-game" }));

            Assert.Single(LoaderScan.Detect(dir, Game("some-game", "frostbite", steamAppId: "123")));
            Assert.Single(LoaderScan.Detect(dir, Game("some-game", "frostbite")));
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
            Feed(Loader("steam-only-tool", "frostbite", "tool.exe", steamAppId: "123"));

            Assert.Single(LoaderScan.Detect(dir, Game("some-game", "frostbite", steamAppId: "123")));
            Assert.Empty(LoaderScan.Detect(dir, Game("some-game", "frostbite")));
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
            Feed(Loader("dual-pin", "frostbite", "tool.exe", steamAppId: "123", gameIds: new[] { "ea-only-game" }));

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
            Feed(Loader("frostbite-wide", "frostbite", "frostbite_loader.exe"));

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
            Feed(Loader("madden-roster-tool", "frostbite", "roster_tool.exe", gameIds: new[] { "madden-nfl-27" }));

            Assert.Empty(LoaderScan.Detect(dir, Game("madden-nfl-27", "custom")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // A game with no engine has no mod lane, so no loader applies, engine-wide or pinned.
    [Fact]
    public void A_game_with_no_engine_gets_no_loaders()
        => Assert.Empty(LoaderScan.BanSafeFor(new GameEntry { Id = "elden-ring", SteamAppId = "1245620", Engine = null }));
}
