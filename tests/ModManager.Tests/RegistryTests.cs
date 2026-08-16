using ModManager.Core;

namespace ModManager.Tests;

// Ports registry-core.test.js — pure registry operations (active-game selection, upsert).
public class RegistryTests
{
    private static GameEntry G(string id) => new()
    {
        Id = id,
        GameName = id,
        GameRoot = "X",
        ModLocations = Array.Empty<ModLocation>(),
        FileExtensions = new[] { "pak" },
        GroupingRule = "filename_no_ext",
    };

    // A registered game with a real install folder (and optionally a Steam id) — the shape
    // FindRegistered has to reason about.
    private static GameEntry G(string id, string root, string? steamAppId = null) => new()
    {
        Id = id,
        GameName = id,
        GameRoot = root,
        SteamAppId = steamAppId,
        ModLocations = Array.Empty<ModLocation>(),
        FileExtensions = new[] { "pak" },
        GroupingRule = "filename_no_ext",
    };

    private static GameRegistry RegOf(params GameEntry[] games)
    {
        var r = Registry.EmptyRegistry();
        foreach (var g in games) r = Registry.UpsertGame(r, g);
        return r;
    }

    [Fact]
    public void EmptyRegistry_shape()
    {
        var r = Registry.EmptyRegistry();
        Assert.Equal(1, r.Version);
        Assert.Null(r.ActiveGameId);
        Assert.Empty(r.Games);
    }

    [Fact]
    public void Upsert_adds_and_sets_active_when_first()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        Assert.Single(r.Games);
        Assert.Equal("a", r.ActiveGameId);
    }

    [Fact]
    public void Upsert_updates_existing_by_id()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.UpsertGame(r, new GameEntry { Id = "a", GameName = "Renamed" });
        Assert.Single(r.Games);
        Assert.Equal("Renamed", r.Games[0].GameName);
    }

    [Fact]
    public void GetActiveGame_falls_back_to_first()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.UpsertGame(r, G("b"));
        r.ActiveGameId = "missing";
        Assert.Equal("a", Registry.GetActiveGame(r)!.Id);
    }

    [Fact]
    public void SetActiveGame_ignores_unknown_id()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        Assert.Equal("a", Registry.SetActiveGame(r, "nope").ActiveGameId);
    }

    [Fact]
    public void SetActiveGame_switches_to_a_known_id()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.UpsertGame(r, G("b"));
        Assert.Equal("b", Registry.SetActiveGame(r, "b").ActiveGameId);
    }

    [Fact]
    public void RemoveGame_drops_a_non_active_game_and_keeps_active()
    {
        var r = Registry.UpsertGame(Registry.UpsertGame(Registry.EmptyRegistry(), G("a")), G("b"));
        r = Registry.RemoveGame(r, "b");
        Assert.Single(r.Games);
        Assert.Equal("a", r.Games[0].Id);
        Assert.Equal("a", r.ActiveGameId);
    }

    [Fact]
    public void RemoveGame_reassigns_active_when_the_active_game_is_removed()
    {
        var r = Registry.UpsertGame(Registry.UpsertGame(Registry.EmptyRegistry(), G("a")), G("b"));
        r = Registry.RemoveGame(r, "a"); // 'a' was active (first added)
        Assert.Single(r.Games);
        Assert.Equal("b", r.ActiveGameId);
    }

    [Fact]
    public void RemoveGame_last_game_clears_active()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.RemoveGame(r, "a");
        Assert.Empty(r.Games);
        Assert.Null(r.ActiveGameId);
    }

    [Fact]
    public void RemoveGame_unknown_id_is_a_noop()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.RemoveGame(r, "nope");
        Assert.Single(r.Games);
        Assert.Equal("a", r.ActiveGameId);
    }

    [Fact]
    public void FindRegistered_matches_an_exact_game_root()
    {
        var r = RegOf(G("windrose", @"C:\Games\Windrose"));
        Assert.Equal("windrose", Registry.FindRegistered(r, @"C:\Games\Windrose", null)?.Id);
    }

    [Fact]
    public void FindRegistered_matches_a_trailing_separator_variant()
    {
        var r = RegOf(G("windrose", @"C:\Games\Windrose"));
        Assert.Equal("windrose", Registry.FindRegistered(r, @"C:\Games\Windrose\", null)?.Id);
    }

    [Fact]
    public void FindRegistered_matches_a_case_different_variant()
    {
        var r = RegOf(G("windrose", @"C:\Games\Windrose"));
        Assert.Equal("windrose", Registry.FindRegistered(r, @"c:\games\windrose", null)?.Id);
    }

    // The case an id-based guard would miss: same install folder, typed in under a different name,
    // so EnginePresets.UniqueId would hand back a brand new id and the add would look novel.
    [Fact]
    public void FindRegistered_matches_the_same_folder_registered_under_a_different_name()
    {
        var r = RegOf(G("windrose", @"C:\Games\Windrose"));
        var hit = Registry.FindRegistered(r, @"C:\Games\Windrose", null);
        Assert.Equal("windrose", hit?.Id);
        Assert.NotEqual("windrose-the-cartographers-tale", hit?.Id);
    }

    [Fact]
    public void FindRegistered_falls_back_to_a_matching_steam_app_id()
    {
        var r = RegOf(G("windrose", @"C:\Games\Windrose", "1234"));
        Assert.Equal("windrose", Registry.FindRegistered(r, @"D:\SteamLibrary\Windrose", "1234")?.Id);
    }

    [Fact]
    public void FindRegistered_returns_null_for_a_genuinely_different_game()
    {
        var r = RegOf(G("windrose", @"C:\Games\Windrose", "1234"));
        Assert.Null(Registry.FindRegistered(r, @"C:\Games\Other", "9999"));
        Assert.Null(Registry.FindRegistered(r, @"C:\Games\Other", null));
    }

    [Fact]
    public void FindRegistered_blank_root_matches_nothing_even_against_a_blank_stored_root()
    {
        var r = RegOf(G("ghost", ""));
        Assert.Null(Registry.FindRegistered(r, "", null));
        Assert.Null(Registry.FindRegistered(r, null, null));
        Assert.Null(Registry.FindRegistered(r, "   ", null));
    }

    [Fact]
    public void FindRegistered_empty_steam_app_id_does_not_match_an_empty_stored_id()
    {
        var r = RegOf(G("windrose", @"C:\Games\Windrose", ""));
        Assert.Null(Registry.FindRegistered(r, @"C:\Games\Other", ""));
        Assert.Null(Registry.FindRegistered(r, @"C:\Games\Other", null));
    }

    [Fact]
    public void FindRegistered_on_an_empty_registry_is_null()
    {
        Assert.Null(Registry.FindRegistered(Registry.EmptyRegistry(), @"C:\Games\Windrose", "1234"));
    }
}
