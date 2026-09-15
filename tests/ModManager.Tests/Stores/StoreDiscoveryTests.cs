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

    // F4: a re-registration (a redetect, a manual re-add, a registry edit) can land a game with no
    // EaContentId recorded at all — the eaIds check above can't see it. Folder and manifest-id are the
    // two other facts that already say "this install is this registered game."

    [Fact]
    public void An_ea_install_is_not_offered_when_its_folder_is_already_registered_under_another_id()
    {
        // No EaContentId on the registration — only the folder ties it back to this install.
        var registered = new[] { new GameEntry { Id = "some-other-id", GameRoot = @"C:\Games\Madden NFL 27" } };
        // Trailing separator + nothing else differs, to prove the comparison normalizes it away.
        var install = new InstalledGame("ea", "16425895", "Madden NFL 27", @"C:\Games\Madden NFL 27\");
        Assert.Empty(StoreDiscovery.Offerable(new[] { install }, registered, Manifest));
    }

    [Fact]
    public void An_ea_install_is_not_offered_when_its_manifest_id_is_already_registered()
    {
        // No EaContentId, and a DIFFERENT folder — only the manifest id (madden-nfl-27) ties it back.
        var registered = new[] { new GameEntry { Id = "madden-nfl-27", GameRoot = @"C:\Elsewhere" } };
        var install = new InstalledGame("ea", "16425895", "Madden NFL 27", @"C:\Games\Madden NFL 27");
        Assert.Empty(StoreDiscovery.Offerable(new[] { install }, registered, Manifest));
    }
}
