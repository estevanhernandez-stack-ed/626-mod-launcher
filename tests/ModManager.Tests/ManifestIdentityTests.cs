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
