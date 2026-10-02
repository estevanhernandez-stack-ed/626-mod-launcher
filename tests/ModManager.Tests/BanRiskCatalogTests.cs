using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

// In the DisableParallelization "ManifestState" collection (with every other SetRemote-mutating
// test) so the process-global EffectiveManifest remote never races a parity test.
[Collection("ManifestState")]
public class BanRiskCatalogTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    [Fact]
    public void ByAppId_resolves_a_flagged_game_and_defaults_None()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "risky", Name = "Risky", Stores = new StoreIds { SteamAppId = "111" }, BanRisk = "high" },
                new GameManifestEntry { Id = "safe", Name = "Safe", Stores = new StoreIds { SteamAppId = "222" } },
            },
        });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.ByAppId("111"));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId("222"));  // present, no flag
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId("999"));  // absent
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId(null));
    }

    private static GameManifestEntry Entry(string id, string? steam, string? risk)
        => new() { Id = id, Name = id, Stores = new StoreIds { SteamAppId = steam }, BanRisk = risk };

    private static GameEntry Game(string id, string? steam)
        => new() { Id = id, GameName = id, SteamAppId = steam };

    [Fact]
    public void Effective_finds_a_flagged_game_that_has_no_Steam_id()
    {
        // The defect: a game registered from the EA app carries no Steam id, and ByAppId-only
        // resolution read None for it.
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("some-ea-game", "555", "high") } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("some-ea-game", null)));
    }

    [Fact]
    public void Effective_matches_ByAppId_for_Steam_games()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { Entry("risky", "111", "high"), Entry("mid", "333", "medium"), Entry("safe", "222", null) },
        });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("my-own-id", "111")));
        Assert.Equal(GameBanRisk.Medium, BanRiskCatalog.Effective(Game("my-own-id", "333")));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("my-own-id", "222")));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("my-own-id", "999")));
    }

    [Fact]
    public void Effective_takes_the_highest_source_in_both_directions()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { Entry("by-steam-medium", "700", "medium"), Entry("by-id-high", "701", "high"),
                            Entry("by-steam-high", "702", "high"), Entry("by-id-low", "703", "low") },
        });

        // Steam id says medium, id says high.
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("by-id-high", "700")));
        // Steam id says high, id says low.
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("by-id-low", "702")));
    }

    [Theory]
    [InlineData("ea-sports-college-football-27", null)]
    [InlineData("madden-nfl-27", null)]
    [InlineData("some-legacy-id", "4032350")]
    [InlineData("some-legacy-id", "3940610")]
    public void The_compiled_floor_holds_with_no_remote_feed(string id, string? steam)
    {
        EffectiveManifest.SetRemote(null);

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game(id, steam)));
    }

    [Fact]
    public void A_feed_cannot_lower_the_compiled_floor()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("madden-nfl-27", "3940610", "low") } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("madden-nfl-27", "3940610")));
    }

    [Fact]
    public void Id_matching_ignores_case_and_does_not_match_a_prefix()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("Risky-Game", null, "high") } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("risky-game", null)));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("risky-game-2", null)));
        EffectiveManifest.SetRemote(null);
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("madden-nfl-27-2", null)));
    }

    [Fact]
    public void Effective_sees_a_feed_change_after_a_first_resolve()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("changing", null, "high") } });
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("changing", null)));

        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("changing", null, "medium") } });
        Assert.Equal(GameBanRisk.Medium, BanRiskCatalog.Effective(Game("changing", null)));
    }

    // A second store copy of a game the user already has is renamed "<id>-2" by EnginePresets.UniqueId,
    // so the EA copy of a dual-store game never carried the manifest id. These resolve it through the
    // store identity it does carry (ManifestIdLookup.IdsFor), never by trimming the suffix: the test
    // above still holds, so a bare "-2" id with no store identity matches nothing.
    private static GameEntry EaGame(string id, string eaContentId)
        => new() { Id = id, GameName = id, EaContentId = eaContentId };

    private static GameManifestEntry EaEntry(string id, string? steam, string eaContentId, string? risk)
        => new() { Id = id, Name = id, Stores = new StoreIds { SteamAppId = steam, EaContentId = eaContentId }, BanRisk = risk };

    [Fact]
    public void The_ea_copy_of_a_dual_store_game_gets_the_feeds_risk_through_its_ea_content_id()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { EaEntry("some-game", "123", "ea.content.1", "high") } });

        var steamCopy = new GameEntry { Id = "some-game", GameName = "some-game", SteamAppId = "123" };
        var eaId = EnginePresets.UniqueId("some-game", new[] { steamCopy.Id });
        Assert.Equal("some-game-2", eaId);   // what the registry actually does to the second copy

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(steamCopy));
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(EaGame(eaId, "ea.content.1")));
    }

    // The compiled floor exists for the games the launcher itself writes files for, and must hold with
    // no feed at all. The embedded snapshot carries neither EA title, so nothing could map an EA
    // content id to a manifest id there; the floor is keyed by EA content id directly. Content ids as
    // read from both installs' installerdata.xml (EA football grand plan, K3).
    [Theory]
    [InlineData("madden-nfl-27-2", "16425895")]
    [InlineData("ea-sports-college-football-27-2", "16425899")]
    [InlineData("whatever-the-registration-is-called", "16425895")]
    public void The_compiled_floor_holds_for_an_ea_copy_with_no_remote_feed(string id, string eaContentId)
    {
        EffectiveManifest.SetRemote(null);

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(EaGame(id, eaContentId)));
    }

    // A feed saying "low" for the EA copy cannot lower the floor either, by any route.
    [Fact]
    public void A_feed_cannot_lower_the_floor_for_an_ea_copy()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { EaEntry("madden-nfl-27", "3940610", "16425895", "low") } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(EaGame("madden-nfl-27-2", "16425895")));
    }

    // An EA content id the feed does not know resolves to nothing extra: no guessing.
    [Fact]
    public void An_unknown_ea_content_id_adds_no_risk()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { EaEntry("some-game", null, "ea.content.1", "high") } });

        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(EaGame("some-game-2", "ea.content.unknown")));
    }

    // K19 is open: a title might ship under a second content id. When the feed names one for a floor
    // game, the floor follows it through the feed's mapping even if that entry carries no risk itself.
    [Fact]
    public void A_content_id_the_feed_names_for_a_floor_game_carries_the_floor()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { EaEntry("madden-nfl-27", null, "alt.content.id", null) } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(EaGame("madden-nfl-27-2", "alt.content.id")));
    }
}
