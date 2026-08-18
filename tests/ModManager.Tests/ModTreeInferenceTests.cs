using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The inferred half of the mod-provenance design, checked against the real shape of Este's Monster
/// Hunter Wilds install. 626 listed a mod per file, so `mhwilds_overlay.lua` appeared as a row while
/// `mhwilds_overlay/` was invisible beside it, and `_CatLib/` (33 files) did not appear at all.
/// </summary>
public class ModTreeInferenceTests
{
    // Verbatim from reframework/autorun on the real install.
    private static readonly string[] RealFiles =
    {
        "KittyBig.lua", "disable_lens.lua", "mhwilds_disable_postprocessing.lua",
        "mhwilds_overlay.lua", "reframework-d2d.lua", "skip_intro_logos.lua",
    };
    private static readonly string[] RealDirs = { "_CatLib", "mhwilds_overlay", "utility" };

    private static InferredMod Row(IReadOnlyList<InferredMod> rows, string key)
        => Assert.Single(rows, r => r.Key == key);

    [Fact]
    public void The_real_folder_produces_the_rows_the_design_says_it_should()
    {
        var rows = ModTreeInference.Group(RealFiles, RealDirs);

        // Six scripts, one of which pairs with its folder, plus two unpaired libraries.
        Assert.Equal(8, rows.Count);
        Assert.Equal(InferredKind.ScriptWithFolder, Row(rows, "mhwilds_overlay").Kind);
        Assert.Equal(InferredKind.Script, Row(rows, "KittyBig").Kind);
        Assert.Equal(InferredKind.Library, Row(rows, "_CatLib").Kind);
        Assert.Equal(InferredKind.Library, Row(rows, "utility").Kind);
    }

    [Fact]
    public void A_script_and_its_folder_are_one_row_not_two()
    {
        // The case the old listing got visibly wrong: turning off the row left most of the mod there.
        var row = Row(ModTreeInference.Group(RealFiles, RealDirs), "mhwilds_overlay");

        Assert.Equal(new[] { "mhwilds_overlay.lua", "mhwilds_overlay" }, row.Members);
    }

    [Fact]
    public void A_library_is_shown_but_never_switchable()
    {
        var rows = ModTreeInference.Group(RealFiles, RealDirs);

        Assert.False(Row(rows, "_CatLib").Togglable);
        Assert.True(Row(rows, "KittyBig").Togglable);
        Assert.True(Row(rows, "mhwilds_overlay").Togglable);
    }

    [Fact]
    public void A_folder_claimed_by_an_install_record_is_not_inferred()
    {
        // A tracked mod knows its own files exactly. Inference is only for what arrived some other way.
        var rows = ModTreeInference.Group(RealFiles, RealDirs, claimedRelPaths: new[] { @"_CatLib\action.lua" });

        Assert.DoesNotContain(rows, r => r.Key == "_CatLib");
        Assert.Contains(rows, r => r.Key == "utility"); // untouched
    }

    [Fact]
    public void One_claimed_file_speaks_for_its_whole_top_level_entry()
    {
        // A manifest lists 33 paths under _CatLib; matching on the first segment means one is enough,
        // and inference never half-claims a folder a tracked mod owns.
        var rows = ModTreeInference.Group(
            new[] { "KittyBig.lua" }, new[] { "_CatLib" },
            claimedRelPaths: new[] { "_CatLib/cache.lua" });

        Assert.Equal("KittyBig", Assert.Single(rows).Key);
    }

    [Fact]
    public void A_claimed_bare_script_drops_out_too()
        => Assert.Empty(ModTreeInference.Group(new[] { "KittyBig.lua" }, null, new[] { "KittyBig.lua" }));

    [Fact]
    public void Pairing_ignores_case_and_extension()
    {
        var rows = ModTreeInference.Group(new[] { "MyMod.LUA" }, new[] { "mymod" });

        Assert.Equal(InferredKind.ScriptWithFolder, Assert.Single(rows).Kind);
    }

    [Fact]
    public void An_empty_folder_produces_nothing()
    {
        Assert.Empty(ModTreeInference.Group(null, null));
        Assert.Empty(ModTreeInference.Group(Array.Empty<string>(), Array.Empty<string>()));
    }

    [Fact]
    public void A_pak_folder_still_lists_one_row_per_file()
    {
        // The majority case must not move. Four paks are four mods, exactly as before.
        var rows = ModTreeInference.Group(
            new[] { "Shop Tweaks - Everything.pak", "Shop Tweaks - Consumables Only.pak" }, null);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(InferredKind.Script, r.Kind));
        Assert.All(rows, r => Assert.True(r.Togglable));
    }
}

/// <summary>
/// Disabling MOVES a mod's files to the holding folder, so the mod location alone does not describe
/// the library. Found by running the inference over a real tree where <c>mhwilds_overlay.lua</c> sat
/// in holding while <c>mhwilds_overlay/</c> did not — which made a disabled mod read as an
/// untogglable library and stranded it.
/// </summary>
public class ModTreeInferenceDisabledTests
{
    [Fact]
    public void A_disabled_mod_still_gets_a_row_reading_off()
    {
        // The row a user needs in order to turn it back on must not be the row that disappeared.
        var rows = ModTreeInference.Group(
            topLevelFiles: new[] { "KittyBig.lua" }, topLevelDirs: null,
            claimedRelPaths: null, disabledKeys: new[] { "skip_intro_logos" });

        var off = Assert.Single(rows, r => r.Key == "skip_intro_logos");
        Assert.False(off.Enabled);
        Assert.True(off.Togglable);
        Assert.True(Assert.Single(rows, r => r.Key == "KittyBig").Enabled);
    }

    [Fact]
    public void A_disabled_paired_mod_does_not_degrade_into_a_library()
    {
        // The exact real case. Without the holding read, mhwilds_overlay/ is an orphan folder, rule 3
        // calls it a library, and the mod loses its switch.
        var rows = ModTreeInference.Group(
            topLevelFiles: Array.Empty<string>(),
            topLevelDirs: new[] { "mhwilds_overlay", "_CatLib" },
            claimedRelPaths: null,
            disabledKeys: new[] { "mhwilds_overlay" });

        var overlay = Assert.Single(rows, r => r.Key == "mhwilds_overlay");
        Assert.Equal(InferredKind.ScriptWithFolder, overlay.Kind);
        Assert.False(overlay.Enabled);
        Assert.True(overlay.Togglable);

        // The genuine library is still a library.
        Assert.Equal(InferredKind.Library, Assert.Single(rows, r => r.Key == "_CatLib").Kind);
    }

    [Fact]
    public void An_enabled_mod_is_never_listed_twice_by_a_stale_holding_entry()
    {
        // A leftover holding folder for a mod that is back on disk must not produce a second row.
        var rows = ModTreeInference.Group(
            new[] { "KittyBig.lua" }, null, null, disabledKeys: new[] { "KittyBig" });

        Assert.True(Assert.Single(rows).Enabled);
    }

    [Fact]
    public void A_tracked_mod_is_not_inferred_even_while_disabled()
        => Assert.Empty(ModTreeInference.Group(
            null, null, claimedRelPaths: new[] { "KittyBig.lua" }, disabledKeys: new[] { "KittyBig" }));
}
