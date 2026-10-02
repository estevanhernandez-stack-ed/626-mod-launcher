using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.Transport;

namespace ModManager.Tests;

/// <summary>
/// EA app games' saves: listed by name, backed up, never written, for now.
///
/// <para>Their save files carry no extension (<c>RTG-E</c>, <c>ROSTER-Official</c>,
/// <c>PROFILE-COLLEGE</c>), so the extension-keyed listing showed nothing. And once the feed names their
/// save folder, every existing write path (restore, clone, bundle import, save mods, the machine
/// restore) would reach a folder the EA app syncs to EA's cloud. The owner's recorded decision allows
/// roster writes only, through a gate that is not built yet; until it is, nothing writes.</para>
/// </summary>
[Collection("ManifestState")]
public class EaSavesReadOnlyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ea-saves-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        EffectiveManifest.SetRemote(null);
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static GameEntry Ea(string id = "college-football-27", string? eaContentId = "16425899")
        => new() { Id = id, GameName = id, Engine = "frostbite", EaContentId = eaContentId };

    private static GameEntry Steam(string id, string appId, string engine)
        => new() { Id = id, GameName = id, Engine = engine, SteamAppId = appId };

    // ---- the write policy ------------------------------------------------------------------------

    [Fact]
    public void An_ea_app_game_refuses_writes_and_says_nothing_changed()
    {
        var refusal = SaveWritePolicy.Refusal(Ea());

        Assert.NotNull(refusal);
        Assert.Contains("Nothing was changed", refusal);
        Assert.Equal(SaveWritePolicy.EaNotice, SaveWritePolicy.Notice(Ea()));
    }

    [Fact]
    public void A_steam_game_keeps_every_write()
    {
        var eldenRing = Steam("elden-ring", "1245620", "fromsoft");

        Assert.Null(SaveWritePolicy.Refusal(eldenRing));
        Assert.Null(SaveWritePolicy.Notice(eldenRing));
        Assert.Equal(RestoreParts.Saves | RestoreParts.Mods | RestoreParts.Settings,
            SaveWritePolicy.Permitted(eldenRing, RestoreParts.Saves | RestoreParts.Mods | RestoreParts.Settings));
    }

    // Keyed on store identity: a copy added by hand under the manifest id carries no content id of its
    // own, but it is the same game with the same cloud sync.
    [Fact]
    public void A_game_whose_manifest_entry_is_an_ea_game_refuses_writes_too()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { new GameManifestEntry { Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", Stores = new StoreIds { EaContentId = "16425895" } } },
        });

        Assert.NotNull(SaveWritePolicy.Refusal(Ea("madden-nfl-27", eaContentId: null)));
    }

    [Fact]
    public void The_machine_restore_keeps_an_ea_games_mods_and_settings_but_not_its_saves()
        => Assert.Equal(RestoreParts.Mods | RestoreParts.Settings,
            SaveWritePolicy.Permitted(Ea(), RestoreParts.Saves | RestoreParts.Mods | RestoreParts.Settings));

    [Fact]
    public void No_game_is_no_refusal()
        => Assert.Null(SaveWritePolicy.Refusal(null));

    // ---- the named listing -----------------------------------------------------------------------

    private void Files(params string[] names)
    {
        Directory.CreateDirectory(_dir);
        foreach (var n in names) File.WriteAllText(Path.Combine(_dir, n), "x");
    }

    [Fact]
    public void College_footballs_saves_list_by_name_with_their_labels()
    {
        Files("RTG-E", "RTG-E-AUTOSAVE", "ROSTER-Official", "PROFILE-COLLEGE", "UserSettings.dat");

        var listed = SaveManager.ListNamedSaveFiles(_dir, SaveFileKindsCatalog.For(Ea()));

        Assert.Equal(new[] { "PROFILE-COLLEGE", "ROSTER-Official", "RTG-E", "RTG-E-AUTOSAVE" }, listed.Select(f => f.Name));
        Assert.Equal("Road to Glory career", listed.Single(f => f.Name == "RTG-E").TypeLabel);
        Assert.Equal("Roster", listed.Single(f => f.Name == "ROSTER-Official").TypeLabel);
        Assert.Equal("Profile", listed.Single(f => f.Name == "PROFILE-COLLEGE").TypeLabel);
        Assert.DoesNotContain(listed, f => f.Name == "UserSettings.dat");   // settings, not a save (not synced either)
    }

    [Fact]
    public void Maddens_franchise_career_lists_as_one()
    {
        Files("CAREER-TEST", "PROFILE-MADDEN", "ROSTER-Official");

        var listed = SaveManager.ListNamedSaveFiles(_dir, SaveFileKindsCatalog.For(Ea("madden-nfl-27", "16425895")));

        Assert.Equal("Franchise career", listed.Single(f => f.Name == "CAREER-TEST").TypeLabel);
        Assert.Equal(3, listed.Count);
    }

    // The extension-keyed listing is what the panel had: it can never see a file with no extension.
    [Fact]
    public void The_extension_listing_sees_none_of_them()
    {
        Files("RTG-E", "ROSTER-Official", "PROFILE-COLLEGE");

        Assert.Empty(SaveManager.ListSaveFiles(_dir, GameSaveTypesCatalog.Resolve(Ea()).SaveTypes));
    }

    [Fact]
    public void Named_kinds_are_not_save_types_so_clone_and_per_type_restore_stay_off()
        => Assert.Empty(GameSaveTypesCatalog.Resolve(Ea()).SaveTypes);

    [Fact]
    public void Other_engines_declare_no_named_kinds()
    {
        Assert.Empty(SaveFileKindsCatalog.For(Steam("elden-ring", "1245620", "fromsoft")));
        Assert.Empty(SaveFileKindsCatalog.For(new GameEntry { Id = "x" }));
    }

    [Fact]
    public void A_missing_folder_or_no_kinds_lists_nothing()
    {
        Assert.Empty(SaveManager.ListNamedSaveFiles(Path.Combine(_dir, "absent"), SaveFileKindsCatalog.For(Ea())));
        Files("RTG-E");
        Assert.Empty(SaveManager.ListNamedSaveFiles(_dir, Array.Empty<SaveFileKind>()));
    }

    [Fact]
    public void Matching_ignores_case_and_needs_the_whole_prefix()
    {
        Files("rtg-e", "RTGX", "ROSTER");

        var listed = SaveManager.ListNamedSaveFiles(_dir, SaveFileKindsCatalog.For(Ea()));

        Assert.Equal(new[] { "rtg-e" }, listed.Select(f => f.Name));
    }

    // The empty state says "your folder may be wrong" only for a game that declares what to look for.
    [Fact]
    public void An_empty_ea_save_folder_points_at_the_folder_not_at_the_app()
        => Assert.StartsWith("No save files of this game's known types",
            SaveListingEmptyState.MessageFor(folderSet: true, declaresTypes: SaveFileKindsCatalog.For(Ea()).Count > 0));
}
