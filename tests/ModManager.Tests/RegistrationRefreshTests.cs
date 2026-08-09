using ModManager.Core;

namespace ModManager.Tests;

// A game's definition is copied out of the engine preset ONCE, when the user adds it, and frozen
// into games.json. The manifest feed keeps improving those definitions — and nothing ever re-applied
// a correction to a game already registered. That silently voids the feed's core promise ("a new game
// on a known engine needs no app release") for exactly the users who have had the launcher longest.
//
// Measured: a Cyberpunk 2077 registration carried fileExtensions ["pak"] — the custom preset's
// default — while the manifest said ["archive"]. Scanner builds its file-matching regex from the
// stored value, so 194 .archive mods on disk showed as ZERO mods in the launcher.
//
// The rule: prefer the manifest when the stored value is still the UNTOUCHED preset default. A user
// who deliberately set a value keeps it; a user carrying a stale snapshot gets the correction.
public class RegistrationRefreshTests
{
    [Fact]
    public void A_stale_preset_snapshot_takes_the_manifest_correction()
    {
        // The real case: registered as "custom" (default ["pak"]), manifest later learned ["archive"].
        var effective = RegistrationRefresh.Extensions(
            stored: new[] { "pak" }, presetDefault: new[] { "pak" }, manifest: new[] { "archive" });

        Assert.Equal(new[] { "archive" }, effective);
    }

    // The whole safety of the heuristic. A value the user chose is evidence about THEIR install that
    // the manifest cannot have; overriding it would be the launcher deciding it knows better.
    [Fact]
    public void A_value_the_user_changed_is_never_overridden()
    {
        var effective = RegistrationRefresh.Extensions(
            stored: new[] { "smpcmod", "suit" }, presetDefault: new[] { "pak" }, manifest: new[] { "archive" });

        Assert.Equal(new[] { "smpcmod", "suit" }, effective);
    }

    [Fact]
    public void With_nothing_to_say_the_manifest_changes_nothing()
    {
        Assert.Equal(new[] { "pak", "ucas", "utoc" }, RegistrationRefresh.Extensions(
            new[] { "pak", "ucas", "utoc" }, new[] { "pak", "ucas", "utoc" }, manifest: null));

        Assert.Equal(new[] { "pak" }, RegistrationRefresh.Extensions(
            new[] { "pak" }, new[] { "pak" }, manifest: Array.Empty<string>()));
    }

    // Engines whose preset legitimately has NO extensions (fromsoft, decima — folder-based) still
    // count as untouched, so a manifest that later learns a real extension list can reach them.
    [Fact]
    public void An_empty_preset_default_is_still_an_untouched_snapshot()
    {
        var effective = RegistrationRefresh.Extensions(
            stored: Array.Empty<string>(), presetDefault: Array.Empty<string>(), manifest: new[] { "archive" });

        Assert.Equal(new[] { "archive" }, effective);
    }

    // Order and case are not choices the user made — ["ucas","pak"] is the same snapshot as
    // ["pak","ucas"], and a stored "PAK" is the preset's "pak". Treating either as customisation
    // would strand the registration forever on a difference that means nothing.
    [Fact]
    public void Order_and_case_do_not_count_as_customisation()
    {
        foreach (var stored in new[] { new[] { "ucas", "pak" }, new[] { "PAK", "UCAS" } })
            Assert.Equal(new[] { "archive" },
                RegistrationRefresh.Extensions(stored, new[] { "pak", "ucas" }, new[] { "archive" }));
    }

    // Stray whitespace is not a choice either, and RegistrationChange.SameExtensions already trims —
    // so without a SHARED helper the two files disagree about the same concept. That disagreement ends
    // self-healing silently: an edit dialog round-trips a text field into [" pak"], the planner reports
    // NO change and pins NOTHING, and yet the untouched-default test now says customised. The manifest
    // stops reaching the game, 194 mods go invisible again, and there is no marker to explain why.
    [Fact]
    public void Padding_does_not_count_as_customisation_either()
    {
        var effective = RegistrationRefresh.Extensions(
            stored: new[] { " pak" }, presetDefault: new[] { "pak" }, manifest: new[] { "archive" });

        Assert.Equal(new[] { "archive" }, effective);
    }

    // A stored list that merely CONTAINS the preset's values is a customisation, not a snapshot.
    [Fact]
    public void A_superset_of_the_preset_default_is_a_customisation()
    {
        var stored = new[] { "pak", "ucas", "myown" };

        Assert.Equal(stored, RegistrationRefresh.Extensions(stored, new[] { "pak", "ucas" }, new[] { "archive" }));
    }

    // ---- Grouping rule: same freeze, same rule ----

    [Fact]
    public void A_stale_grouping_rule_takes_the_manifest_correction()
        => Assert.Equal("extension", RegistrationRefresh.Grouping("filename_no_ext", "filename_no_ext", "extension"));

    [Fact]
    public void A_grouping_rule_the_user_changed_is_kept()
        => Assert.Equal("by_folder", RegistrationRefresh.Grouping("by_folder", "filename_no_ext", "extension"));

    [Fact]
    public void A_manifest_silent_on_grouping_changes_nothing()
    {
        Assert.Equal("filename_no_ext", RegistrationRefresh.Grouping("filename_no_ext", "filename_no_ext", null));
        Assert.Equal("filename_no_ext", RegistrationRefresh.Grouping("filename_no_ext", "filename_no_ext", "   "));
    }

    // Nothing here writes: this is read at context-build time so games.json is never rewritten behind
    // the user's back. A correction that reaches into their file would need consent; one that only
    // changes what the scanner LOOKS FOR does not.
    [Fact]
    public void The_stored_list_instance_is_never_mutated()
    {
        var stored = new[] { "pak" };

        RegistrationRefresh.Extensions(stored, new[] { "pak" }, new[] { "archive" });

        Assert.Equal(new[] { "pak" }, stored);
    }

    // ---- the explicit marker beats the inference ----

    // A1's one documented blind spot: a user who deliberately picks a value that happens to EQUAL the
    // preset default is indistinguishable from one who never touched it, so the manifest overrides a
    // real choice. The marker is the only signal in the system that is not an inference, so it wins.
    [Fact]
    public void A_marked_field_is_kept_even_when_it_equals_the_preset_default()
    {
        var effective = RegistrationRefresh.Extensions(
            stored: new[] { "pak" }, presetDefault: new[] { "pak" },
            manifest: new[] { "archive" }, userSet: true);

        Assert.Equal(new[] { "pak" }, effective);
    }

    [Fact]
    public void An_unmarked_field_still_self_heals()
    {
        var effective = RegistrationRefresh.Extensions(
            stored: new[] { "pak" }, presetDefault: new[] { "pak" },
            manifest: new[] { "archive" }, userSet: false);

        Assert.Equal(new[] { "archive" }, effective);
    }

    [Fact]
    public void A_marked_grouping_rule_is_kept_even_when_it_equals_the_preset_default()
    {
        Assert.Equal("filename_no_ext", RegistrationRefresh.Grouping(
            "filename_no_ext", "filename_no_ext", "extension", userSet: true));

        Assert.Equal("extension", RegistrationRefresh.Grouping(
            "filename_no_ext", "filename_no_ext", "extension", userSet: false));
    }

    // The default keeps every pre-existing call site and all ten original tests behaving identically.
    [Fact]
    public void The_marker_defaults_to_absent()
    {
        Assert.Equal(new[] { "archive" },
            RegistrationRefresh.Extensions(new[] { "pak" }, new[] { "pak" }, new[] { "archive" }));
    }
}
