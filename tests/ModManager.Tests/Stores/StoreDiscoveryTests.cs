using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

public class StoreDiscoveryTests
{
    private static readonly GameManifestEntry[] Manifest =
    {
        new() { Id = "madden-nfl-27", Name = "Madden NFL 27", Stores = new StoreIds { EaContentId = "16425895" } },
    };

    private static InstalledGame Steam(string id) => new("steam", id, "S" + id, "C:");
    private static InstalledGame Ea(string id) => new("ea", id, "E" + id, "C:");

    [Fact]
    public void Steam_games_behave_as_they_did()
    {
        var registered = new[] { new GameEntry { Id = "a", SteamAppId = "1" } };
        var offered = StoreDiscovery.Offerable(new[] { Steam("1"), Steam("2") }, registered, Manifest);
        Assert.Equal(new[] { "2" }, offered.Select(g => g.AppId));
    }

    [Fact]
    public void A_curated_ea_game_is_offered_until_it_is_registered()
    {
        Assert.Single(StoreDiscovery.Offerable(new[] { Ea("16425895") }, Array.Empty<GameEntry>(), Manifest));

        var registered = new[] { new GameEntry { Id = "madden-nfl-27", EaContentId = "16425895" } };
        Assert.Empty(StoreDiscovery.Offerable(new[] { Ea("16425895") }, registered, Manifest));
    }

    [Fact]
    public void An_uncurated_ea_game_is_not_offered()
        => Assert.Empty(StoreDiscovery.Offerable(new[] { Ea("555") }, Array.Empty<GameEntry>(), Manifest));

    [Fact]
    public void The_store_kind_is_part_of_the_key()
    {
        // An EA content id and a Steam app id can be the same digits. Neither may hide the other.
        var registeredEa = new[] { new GameEntry { Id = "madden-nfl-27", EaContentId = "16425895" } };
        Assert.Single(StoreDiscovery.Offerable(new[] { Steam("16425895") }, registeredEa, Manifest));

        var registeredSteam = new[] { new GameEntry { Id = "x", SteamAppId = "16425895" } };
        Assert.Single(StoreDiscovery.Offerable(new[] { Ea("16425895") }, registeredSteam, Manifest));
    }
}
