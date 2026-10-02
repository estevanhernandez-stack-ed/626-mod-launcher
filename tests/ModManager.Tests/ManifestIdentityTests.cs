using ModManager.Core;
using ModManager.Core.Characters;
using ModManager.Core.Manifest;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

/// <summary>
/// The entry a game was added AS, recorded at add time (<see cref="GameEntry.ManifestId"/>).
///
/// <para>A second copy with no store id, a GOG or hand-added copy renamed <c>&lt;id&gt;-2</c>, carried
/// nothing that named its game, so no resolver could reach it: no manifest corrections, no save facts,
/// no ban risk by id. The add knew which entry it was adding; now the registration remembers.</para>
/// </summary>
[Collection("ManifestState")]
public class ManifestIdentityTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "mid-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        EffectiveManifest.SetRemote(null);
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    // cyberpunk-2077 is in the embedded snapshot: Steam 1091500, engine custom, modPath archive/pc/mod.
    private static GameEntry GogSecondCopy(string? manifestId = "cyberpunk-2077") => new()
    {
        Id = "cyberpunk-2077-2", GameName = "Cyberpunk 2077", Engine = "custom", ManifestId = manifestId,
        GameRoot = Path.Combine(Path.GetTempPath(), "mid-gog"),
        ModLocations = new[] { new ModLocation("mods", "Mods", EnginePresets.Presets["custom"].ModPath) },
    };

    // ---- recorded at add time --------------------------------------------------------------------

    [Fact]
    public void An_add_that_names_an_entry_records_it_through_the_rename()
    {
        var entry = EnginePresets.BuildGameEntry(
            new GameInput { Id = "cyberpunk-2077", Name = "Cyberpunk 2077", Engine = "custom", GameRoot = @"D:\GOG\Cyberpunk" },
            existingIds: new[] { "cyberpunk-2077" });

        Assert.Equal("cyberpunk-2077-2", entry.Id);
        Assert.Equal("cyberpunk-2077", entry.ManifestId);
    }

    // A name that slugifies to a manifest id is a guess ("doom" for Doom Eternal), not a choice.
    [Fact]
    public void An_add_by_name_records_nothing()
        => Assert.Null(EnginePresets.BuildGameEntry(
            new GameInput { Name = "Cyberpunk 2077", Engine = "custom" }, existingIds: null).ManifestId);

    [Fact]
    public void An_add_naming_an_id_the_manifest_does_not_have_records_nothing()
        => Assert.Null(EnginePresets.BuildGameEntry(
            new GameInput { Id = "my-own-game", Name = "Mine", Engine = "custom" }, existingIds: null).ManifestId);

    // ---- every resolver reads it -----------------------------------------------------------------

    [Fact]
    public void A_store_less_second_copy_resolves_to_its_game()
    {
        var game = GogSecondCopy();

        Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(game)?.Id);
        Assert.Equal("cyberpunk-2077", ManifestIdLookup.ConfirmedEntryFor(game)?.Id);
        Assert.Contains("cyberpunk-2077", ManifestIdLookup.IdsFor(game));
        Assert.True(CyberpunkCharacters.AppliesTo(game));
    }

    [Fact]
    public void Without_the_record_it_resolves_to_nothing_as_before()
    {
        var game = GogSecondCopy(manifestId: null);

        Assert.Null(ManifestIdLookup.EntryFor(game));
        Assert.False(CyberpunkCharacters.AppliesTo(game));
    }

    [Fact]
    public void A_store_less_second_copy_gets_its_manifest_corrections()
        => Assert.EndsWith(Path.Combine("archive", "pc", "mod"), Scanner.GameContext(GogSecondCopy()).Locations[0].Abs);

    [Fact]
    public void A_store_id_still_outranks_the_recorded_id()
    {
        var game = GogSecondCopy();
        game.SteamAppId = "1245620";   // Elden Ring's

        Assert.Equal("elden-ring", ManifestIdLookup.EntryFor(game)?.Id);
    }

    // The save-fact join fails closed on the recorded id the same way it does on the own id.
    [Fact]
    public void A_recorded_id_a_store_id_contradicts_gets_no_save_facts()
    {
        var game = GogSecondCopy();
        game.SteamAppId = "4242424";   // unknown to the feed, and not Cyberpunk's

        Assert.Equal("cyberpunk-2077", ManifestIdLookup.EntryFor(game)?.Id);   // lenient join: still Cyberpunk
        Assert.Null(ManifestIdLookup.ConfirmedEntryFor(game));
    }

    [Fact]
    public void A_store_less_second_copy_of_a_ban_risk_game_gets_its_ban_risk()
    {
        var copy = new GameEntry { Id = "madden-copy-2", GameName = "Madden", Engine = "frostbite", ManifestId = "madden-nfl-27" };

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(copy));   // the compiled floor, by manifest id
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(new GameEntry { Id = "madden-copy-2", Engine = "frostbite" }));
    }

    [Fact]
    public void A_store_less_copy_of_an_ea_game_refuses_save_writes_and_lists_its_kinds()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", Stores = new StoreIds { EaContentId = "16425895" } } },
        });
        var copy = new GameEntry { Id = "madden-copy-2", GameName = "Madden", Engine = "frostbite", ManifestId = "madden-nfl-27" };

        Assert.NotNull(SaveWritePolicy.Refusal(copy));
        Assert.Equal("Franchise career", SaveFileKindsCatalog.For(copy)[0].Label);
    }

    // Fails closed: a recorded EA game refuses writes even when a Steam id names another game.
    [Fact]
    public void A_recorded_ea_game_refuses_writes_even_when_a_steam_id_names_another_game()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", Stores = new StoreIds { EaContentId = "16425895" } } },
        });
        var confused = new GameEntry { Id = "madden-copy-2", Engine = "frostbite", ManifestId = "madden-nfl-27", SteamAppId = "1245620" };

        Assert.Equal("elden-ring", ManifestIdLookup.EntryFor(confused)?.Id);
        Assert.NotNull(SaveWritePolicy.Refusal(confused));
    }

    // ---- review on #362 ---------------------------------------------------------------------------

    // The feed can fold a snapshot entry into its own id (EffectiveManifest.Merge, as skyrim-se went).
    // A record of the old id must still name the game.
    [Fact]
    public void A_recorded_id_the_feed_folded_away_still_names_its_game()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "cyberpunk-2077-ultimate", Name = "Cyberpunk", Engine = "custom", Stores = new StoreIds { SteamAppId = "1091500" } } },
        });
        Assert.DoesNotContain(EffectiveManifest.Current.Games, g => g.Id == "cyberpunk-2077");

        var game = GogSecondCopy();
        Assert.Equal("cyberpunk-2077-ultimate", ManifestIdLookup.EntryFor(game)?.Id);
        Assert.Equal(new[] { "cyberpunk-2077-ultimate" }, ManifestIdLookup.IdsFor(game));
        // And an older registration still carrying the folded id as its own.
        Assert.Equal("cyberpunk-2077-ultimate",
            ManifestIdLookup.EntryFor(new GameEntry { Id = "cyberpunk-2077", Engine = "custom" })?.Id);
    }

    // A recorded copy's own id is a -N rename, and portal-2 is a different game's id. The join and the
    // loader pins name the recorded game only, and agree.
    [Fact]
    public void A_recorded_copy_is_never_its_rename()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "portal", Name = "Portal", Engine = "source" },
                new GameManifestEntry { Id = "portal-2", Name = "Portal 2", Engine = "source" },
            },
        });
        var copy = new GameEntry { Id = "portal-2", GameName = "Portal", Engine = "source", ManifestId = "portal" };

        Assert.Equal("portal", ManifestIdLookup.EntryFor(copy)?.Id);
        Assert.Equal(new[] { "portal" }, ManifestIdLookup.IdsFor(copy));

        // A record that no longer resolves does not fall back to the rename either.
        var stale = new GameEntry { Id = "portal-2", GameName = "Portal", Engine = "source", ManifestId = "portal-gone" };
        Assert.Null(ManifestIdLookup.EntryFor(stale));
    }

    // A store id on the same add that names another game, or that the entry claims differently, would
    // be a contradicted record read fail-closed forever. Not recorded.
    [Fact]
    public void An_add_whose_store_id_contradicts_the_pick_records_nothing()
    {
        GameEntry Add(string? steam) => EnginePresets.BuildGameEntry(
            new GameInput { Id = "cyberpunk-2077", Name = "Cyberpunk 2077", Engine = "custom", SteamAppId = steam }, existingIds: null);

        Assert.Null(Add("1245620").ManifestId);   // Elden Ring's
        Assert.Null(Add("4242424").ManifestId);   // unknown, and not Cyberpunk's
        Assert.Equal("cyberpunk-2077", Add("1091500").ManifestId);
        Assert.Equal("cyberpunk-2077", Add(null).ManifestId);

        // An entry that claims no Steam id cannot disagree with one, but a Steam id naming ANOTHER
        // entry still makes the pick a different game.
        Assert.Null(EnginePresets.BuildGameEntry(
            new GameInput { Id = "minecraft", Name = "Minecraft", Engine = "minecraft", SteamAppId = "1245620" }, existingIds: null).ManifestId);
    }

    // The save kinds take the confirmed join's word, not the raw record, once a store id can contradict.
    [Fact]
    public void A_contradicted_record_lists_no_save_kinds()
    {
        var game = new GameEntry { Id = "madden-copy-2", Engine = "frostbite", ManifestId = "madden-nfl-27", SteamAppId = "4242424" };
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", Stores = new StoreIds { SteamAppId = "3940610", EaContentId = "16425895" } } },
        });

        Assert.Null(ManifestIdLookup.ConfirmedEntryFor(game));
        Assert.Empty(SaveFileKindsCatalog.For(game));
    }

    [Fact]
    public void A_store_less_pick_takes_the_recorded_entrys_nexus_domain()
        => Assert.Equal("cyberpunk2077", EnginePresets.BuildGameEntry(
            new GameInput { Id = "cyberpunk-2077", Name = "Cyberpunk 2077", Engine = "custom" }, existingIds: null).NexusGameDomain);

    // ---- on disk: camelCase, and absent when null -------------------------------------------------

    [Fact]
    public void ManifestId_round_trips_as_camelCase_and_is_omitted_when_null()
    {
        var reg = Registry.EmptyRegistry();
        reg.Games.Add(GogSecondCopy());
        reg.Games.Add(new GameEntry { Id = "plain", GameName = "Plain", Engine = "custom" });

        RegistryStore.Save(_tmp, reg);
        var json = File.ReadAllText(Path.Combine(_tmp, RegistryStore.FileName));

        Assert.Contains("\"manifestId\"", json);
        Assert.DoesNotContain("\"ManifestId\"", json);
        Assert.Equal(1, json.Split("\"manifestId\"").Length - 1);   // the null one writes no key

        var loaded = RegistryStore.Load(_tmp);
        Assert.Equal("cyberpunk-2077", loaded.Games.Single(g => g.Id == "cyberpunk-2077-2").ManifestId);
        Assert.Null(loaded.Games.Single(g => g.Id == "plain").ManifestId);
    }
}
