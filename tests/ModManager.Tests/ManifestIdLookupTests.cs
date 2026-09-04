using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// Naming which curated game a Steam app id refers to.
///
/// <para>A registered game's id is what the launcher joins on to find its curated engine, mod path and
/// ban risk (<c>Scanner.cs</c>). Until now that id came from slugifying whatever display name the user
/// happened to have in the box — so "Minecraft: Java Edition" produced <c>minecraft-java-edition</c>
/// and matched the <c>minecraft</c> entry not at all, silently discarding every curated fact about it.
/// This is the lookup that lets the add path say WHICH game instead of guessing from a name.</para>
/// </summary>
public class ManifestIdLookupTests
{
    private static GameManifest M(params (string Id, string? Steam)[] games) => new()
    {
        Games = games.Select(g => new GameManifestEntry
        {
            Id = g.Id,
            Name = g.Id,
            Stores = new StoreIds { SteamAppId = g.Steam },
        }).ToList(),
    };

    [Fact]
    public void A_known_app_id_names_its_entry()
        => Assert.Equal("elden-ring", ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), "1245620"));

    [Fact]
    public void An_unknown_app_id_names_nothing()
        => Assert.Null(ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), "999999"));

    [Fact]
    public void An_entry_with_no_Steam_id_is_never_matched_by_one()
    {
        // Minecraft has no Steam id at all. Asking by app id must not reach it - the only honest
        // answer is "no match", which leaves the caller on the name-derived id.
        Assert.Null(ManifestIdLookup.BySteamAppId(M(("minecraft", null)), "1245620"));
    }

    [Fact]
    public void No_manifest_and_no_app_id_are_both_just_null()
    {
        // Runs on every Steam import, including on a machine whose manifest never loaded.
        Assert.Null(ManifestIdLookup.BySteamAppId(null, "1245620"));
        Assert.Null(ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), null));
        Assert.Null(ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), "   "));
    }

    [Fact]
    public void The_first_entry_wins_when_two_claim_one_app_id()
    {
        // The feed's build gate refuses a duplicate app id, so this should not reach a user. Pinned
        // anyway because "whatever the enumeration happened to reach first" is exactly the silent
        // behaviour that gate exists to stop, and a lookup should not reintroduce it by accident.
        Assert.Equal("first", ManifestIdLookup.BySteamAppId(M(("first", "111"), ("second", "111")), "111"));
    }
}
