using ModManager.Core;

namespace ModManager.Tests;

// Ports popular-games.js — the curated quick-pick catalog for the Add Game wizard. Picking one
// pre-fills the engine, mod folder, and Steam App ID; engine values are ENGINE_PRESETS keys.
public class PopularGamesTests
{
    [Fact]
    public void Catalog_has_the_ten_games_in_order()
    {
        var ids = PopularGames.All.Select(g => g.Id).ToArray();
        Assert.Equal(new[]
        {
            "skyrim-se", "fallout-4", "starfield", "stardew-valley", "rimworld",
            "valheim", "lethal-company", "palworld", "hogwarts-legacy", "cyberpunk-2077",
        }, ids);
    }

    [Fact]
    public void Find_skyrim_returns_bethesda_data_and_app_id()
    {
        var g = PopularGames.Find("skyrim-se");
        Assert.NotNull(g);
        Assert.Equal("Skyrim Special Edition", g!.Name);
        Assert.Equal("bethesda", g.Engine);
        Assert.Equal("Data", g.ModPath);
        Assert.Equal("489830", g.SteamAppId);
        Assert.Null(g.FileExtensions); // no override — uses the bethesda preset's extensions
    }

    [Fact]
    public void Find_unknown_id_returns_null()
    {
        Assert.Null(PopularGames.Find("not-a-real-game"));
        Assert.Null(PopularGames.Find(""));
        Assert.Null(PopularGames.Find(null));
    }

    [Fact]
    public void Cyberpunk_carries_the_archive_file_extensions_override()
    {
        var g = PopularGames.Find("cyberpunk-2077");
        Assert.NotNull(g);
        Assert.Equal("custom", g!.Engine);
        Assert.Equal("archive/pc/mod", g.ModPath);
        Assert.Equal(new[] { "archive" }, g.FileExtensions?.ToArray());
        Assert.Equal("1091500", g.SteamAppId);
    }

    [Fact]
    public void Every_engine_key_resolves_to_a_real_engine_preset()
    {
        foreach (var g in PopularGames.All)
            Assert.True(EnginePresets.Presets.ContainsKey(g.Engine),
                $"{g.Id} references unknown engine '{g.Engine}'");
    }

    [Fact]
    public void Every_entry_has_the_required_shape()
    {
        foreach (var g in PopularGames.All)
        {
            Assert.False(string.IsNullOrEmpty(g.Id), g.Name);
            Assert.False(string.IsNullOrEmpty(g.Name), g.Id);
            Assert.False(string.IsNullOrEmpty(g.ModPath), g.Id);
            Assert.False(string.IsNullOrEmpty(g.SteamAppId), g.Id);
        }
    }

    [Fact]
    public void BuildGameEntry_from_a_popular_pick_seeds_path_and_steam_launch()
    {
        var g = PopularGames.Find("valheim")!;
        var e = EnginePresets.BuildGameEntry(
            new GameInput { Name = g.Name, Engine = g.Engine, GameRoot = "C:/g/Valheim", ModPath = g.ModPath, SteamAppId = g.SteamAppId },
            Array.Empty<string>());
        Assert.Equal("valheim", e.Id);
        Assert.Equal("BepInEx/plugins", e.ModLocations[0].Path);
        Assert.Equal("892970", e.SteamAppId);
        Assert.Equal("steam://rungameid/892970", e.LaunchUrl);
    }
}
