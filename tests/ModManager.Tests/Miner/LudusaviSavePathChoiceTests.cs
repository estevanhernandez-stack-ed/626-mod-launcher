using ManifestMiner;

namespace ModManager.Tests.Miner;

/// <summary>
/// Which of a game's Ludusavi paths becomes its <c>saveDirHint</c>.
///
/// <para>This used to be "the first key in a YAML map". Across the 148 shipped hints that produced 24
/// non-Windows paths, 38 config directories and 12 game-install-relative ones — roughly half the feed
/// describing something other than a save folder. Palworld's own hint pointed at the Microsoft Store
/// container rather than the Steam folder the app actually reads.</para>
///
/// <para>It was harmless only because nothing consumed the field. It stops being harmless the moment
/// anything does — and the save-layout work described in
/// <c>docs/superpowers/plans/2026-08-20-save-transport-and-the-data-it-needs.md</c> annotates exactly
/// this folder.</para>
/// </summary>
public class LudusaviSavePathChoiceTests
{
    private static LudusaviSavePath Path(string path, string tag = "save", string? os = "windows", string? store = null)
        => new(path,
               tag.Length == 0 ? Array.Empty<string>() : new[] { tag },
               os is null ? Array.Empty<string>() : new[] { os },
               store is null ? Array.Empty<string>() : new[] { store });

    private static string? HintFor(params LudusaviSavePath[] paths)
        => LudusaviNormalize
            .ToCandidates(new[] { new LudusaviGame("G") { SteamAppId = "1", SaveEntries = paths } })
            .Single().SaveDirHint;

    [Fact]
    public void A_save_beats_a_config_whatever_order_they_arrive_in()
    {
        // Ludusavi's whole tag vocabulary is `save` and `config`. Ordering is YAML map order, which is
        // not a preference and never was.
        Assert.Equal("<winAppData>/G/Saves",
            HintFor(Path("<base>/G/Config", tag: "config"), Path("<winAppData>/G/Saves")));

        Assert.Equal("<winAppData>/G/Saves",
            HintFor(Path("<winAppData>/G/Saves"), Path("<base>/G/Config", tag: "config")));
    }

    [Fact]
    public void A_game_with_only_config_paths_gets_no_hint_rather_than_a_wrong_one()
    {
        // monster-hunter-wilds shipped `<base>/config.ini` as its save folder. Saying nothing is the
        // honest answer; "nobody looked" and "we looked and it is here" must not be the same value.
        Assert.Null(HintFor(Path("<base>/config.ini", tag: "config")));
        Assert.Null(HintFor(Path("<base>/settings", tag: "")));   // untagged is not a save either
    }

    [Fact]
    public void A_path_that_only_applies_to_another_operating_system_is_not_ours()
    {
        // 7-days-to-die shipped `<home>/.config/unity3d/...`, a Linux path, on a Windows-only app.
        Assert.Equal("<winAppData>/7DaysToDie/Saves",
            HintFor(Path("<home>/.config/unity3d/TFP", os: "linux"),
                    Path("<winAppData>/7DaysToDie/Saves", os: "windows")));

        Assert.Null(HintFor(Path("<home>/Library/App Support/G", os: "mac")));
    }

    [Fact]
    public void A_path_with_no_when_clause_applies_everywhere_including_here()
    {
        // Absent `when` means "all", not "none". Treating it as none would drop most of the corpus.
        Assert.Equal("<winAppData>/G/Saves", HintFor(Path("<winAppData>/G/Saves", os: null)));
    }

    [Fact]
    public void The_steam_path_wins_over_the_microsoft_store_container()
    {
        // Palworld, verbatim. The shipped hint was the wgs container; the app reads the Steam folder.
        // `when.store` is the only thing in the data that separates them.
        var wgs = Path("<winLocalAppData>/Packages/PocketpairInc.Palworld_ad4psfrxyesvt/SystemAppData/wgs/<storeUserId>",
                       os: "windows", store: "microsoft");
        var steam = Path("<winLocalAppData>/Pal/Saved/SaveGames/<storeUserId>", os: "windows");

        Assert.Equal("<winLocalAppData>/Pal/Saved/SaveGames/<storeUserId>", HintFor(wgs, steam));
        Assert.Equal("<winLocalAppData>/Pal/Saved/SaveGames/<storeUserId>", HintFor(steam, wgs));
    }

    [Fact]
    public void A_path_inside_the_game_install_loses_to_one_in_the_user_profile()
    {
        // `<base>` is the install directory. A save can live there, but when a user-profile path is
        // also on offer that is the one people mean - and the one that survives a reinstall.
        Assert.Equal("<winDocuments>/My Games/G",
            HintFor(Path("<base>/G/Saved/SaveGames"), Path("<winDocuments>/My Games/G")));
    }

    [Fact]
    public void A_path_naming_our_store_beats_an_unqualified_one_even_from_the_install_folder()
    {
        // Lies of P, verbatim, and the case that found this rule. Its Steam save is declared
        // <base>-relative WITH `when: store: steam`; its macOS save is declared with no `when` at all,
        // so nothing in the data marks it as foreign. Preferring the user-profile path first picked
        // the Mac one. A path that names our store is making a claim about us; a path that names
        // nothing is a fallback.
        var steam = new LudusaviSavePath("<base>/LiesofP/Saved/SaveGames/<storeUserId>",
                                         new[] { "save" }, Array.Empty<string>(), new[] { "steam" });
        var mac = new LudusaviSavePath("<home>/Library/Containers/com.neowiz.game.lop/Data/…/SaveGames",
                                       new[] { "save" }, Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal("<base>/LiesofP/Saved/SaveGames/<storeUserId>", HintFor(steam, mac));
        Assert.Equal("<base>/LiesofP/Saved/SaveGames/<storeUserId>", HintFor(mac, steam));
    }

    [Fact]
    public void A_base_relative_save_is_still_better_than_nothing()
    {
        // Only when it is the only save-tagged option. Preference, not exclusion.
        Assert.Equal("<base>/G/Saved/SaveGames", HintFor(Path("<base>/G/Saved/SaveGames")));
    }

    [Fact]
    public void A_game_with_no_paths_at_all_gets_no_hint()
        => Assert.Null(HintFor());
}
