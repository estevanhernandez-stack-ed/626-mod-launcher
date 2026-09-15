using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests.Stores;

public class EaRegistrationTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-ea-reg-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private static GameInput Input(string root) => new()
    {
        Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", GameRoot = root,
        EaContentId = "16425895",
        LaunchUrl = "origin2://game/launch/?offerIds=16425895",
        DataDir = Path.Combine("L", "626mods", "madden-nfl-27"),
    };

    [Fact]
    public void The_input_lands_on_the_entry_with_no_steam_id_and_no_mod_locations()
    {
        var e = EnginePresets.BuildGameEntry(Input(_root), existingIds: null);

        Assert.Equal("madden-nfl-27", e.Id);
        Assert.Equal("frostbite", e.Engine);
        Assert.Equal("16425895", e.EaContentId);
        Assert.Equal("origin2://game/launch/?offerIds=16425895", e.LaunchUrl);
        Assert.Equal(Path.Combine("L", "626mods", "madden-nfl-27"), e.DataDir);
        Assert.Null(e.SteamAppId);
        Assert.Empty(e.ModLocations);
    }

    [Fact]
    public void A_steam_input_still_derives_its_steam_link_and_one_location()
    {
        var e = EnginePresets.BuildGameEntry(new GameInput { Name = "Palworld", Engine = "custom", GameRoot = _root, SteamAppId = "1623730" }, null);
        Assert.Equal("steam://rungameid/1623730", e.LaunchUrl);
        Assert.Single(e.ModLocations);
        Assert.Null(e.EaContentId);
    }

    [Fact]
    public void The_ea_id_round_trips_through_the_registry_as_camelCase()
    {
        var entry = EnginePresets.BuildGameEntry(Input(_root), null);
        RegistryStore.Save(_root, Registry.UpsertGame(Registry.EmptyRegistry(), entry));

        var json = File.ReadAllText(RegistryStore.PathFor(_root));
        Assert.Contains("\"eaContentId\"", json);
        Assert.DoesNotContain("\"EaContentId\"", json);
        var back = RegistryStore.Load(_root).Games.Single();
        Assert.Equal("16425895", back.EaContentId);
        Assert.Equal(entry.DataDir, back.DataDir);
    }

    [Fact]
    public void A_game_with_no_mod_locations_lists_no_mods_and_does_not_throw()
    {
        var root = Path.Combine(_root, "Madden NFL 27");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Madden27.exe"), "game");
        var entry = EnginePresets.BuildGameEntry(Input(root), null);
        entry.DataDir = Path.Combine(_root, "data");

        Assert.Empty(ModListing.Resolve(entry));
        Assert.Empty(Scanner.GameContext(entry).Locations);
    }
}
