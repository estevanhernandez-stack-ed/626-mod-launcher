using ModManager.Core;

namespace ModManager.Tests;

// The move-or-pin prompt has to name a real folder, a real size, and a real reason it might refuse.
// That is a decision, not a rendering detail — and decisions parked in MainViewModel (14 concrete
// service deps, unconstructible in tests) have repeatedly accumulated defects until someone
// extracted them. This extracts it before it is ever parked.
public class RegistrationChangeTests
{
    private static GameEntry Stored(string root) => new()
    {
        Id = "elden-ring",
        GameName = "ELDEN RING",
        Engine = "fromsoft",
        GameRoot = root,
        FileExtensions = Array.Empty<string>(),
        GroupingRule = "by_folder",
        ModLocations = new[] { new ModLocation("mods", "mods", "mod") },
    };

    private static GameEntry Copy(GameEntry g) => new()
    {
        Id = g.Id, GameName = g.GameName, Engine = g.Engine, GameRoot = g.GameRoot,
        FileExtensions = g.FileExtensions, GroupingRule = g.GroupingRule,
        ModLocations = g.ModLocations, UserSet = g.UserSet,
        SteamAppId = g.SteamAppId, RequiredLauncher = g.RequiredLauncher,
    };

    [Fact]
    public void An_unchanged_entry_changes_and_pins_nothing()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));

        var plan = RegistrationChange.Plan(stored, Copy(stored));

        Assert.Empty(plan.FieldsChanged);
        Assert.Empty(plan.FieldsToPin);
        Assert.Null(plan.DataDir);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void Correcting_the_mod_path_is_reported_and_pinned()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", "Game/mod") };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsChanged);
        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsToPin);
        Assert.Null(plan.DataDir);          // the mod path does not move the data dir
        Assert.True(plan.CanSave);
    }

    // THE identity rule. Id is half the data-dir key, so re-slugging it on a cosmetic rename would
    // silently orphan every disabled mod, profile, and installed tool.
    [Fact]
    public void Renaming_the_game_leaves_the_id_and_the_data_dir_alone()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring (Reforged)";

        var plan = RegistrationChange.Plan(stored, proposed);

        // Assert on the PLANNER, not on the fixture: a rename must imply no data-dir move and no
        // gameRoot change, because the data dir is keyed on (Id, GameRoot) and neither moved.
        Assert.Null(plan.DataDir);
        Assert.DoesNotContain(GameEntry.UserSetGameRoot, plan.FieldsChanged);
        Assert.Empty(plan.Blockers);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void An_attempt_to_change_the_id_is_blocked_outright()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.Id = "elden-ring-2";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.False(plan.CanSave);
        Assert.NotEmpty(plan.Blockers);
    }

    [Fact]
    public void Changing_the_game_folder_produces_a_move_plan_with_real_numbers()
    {
        var oldRoot = Path.Combine(TestSupport.TempDir("rc-old-"), "ELDEN RING");
        Directory.CreateDirectory(oldRoot);
        var stored = Stored(oldRoot);

        // Populate the data dir the stored entry actually resolves to.
        var dataDir = Scanner.DataDirForGame(stored);
        TestSupport.Write(Path.Combine(dataDir, "disabled", "SomeMod.dll"), "held file");

        var proposed = Copy(stored);
        proposed.GameRoot = Path.Combine(TestSupport.TempDir("rc-new-"), "ELDEN RING");
        Directory.CreateDirectory(proposed.GameRoot);

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.NotNull(plan.DataDir);
        Assert.Equal(1, plan.DataDir!.FileCount);
        Assert.True(plan.DataDir.TotalBytes > 0);
        Assert.Contains(GameEntry.UserSetGameRoot, plan.FieldsChanged);
        Assert.True(plan.CanSave);
    }

    // A refusal from the mover must surface, not be swallowed. Swallowing it would let a save proceed
    // into a merge that Plan already decided was unsafe.
    [Fact]
    public void A_refusal_from_the_mover_becomes_a_blocker()
    {
        var oldRoot = Path.Combine(TestSupport.TempDir("rc-old-"), "ELDEN RING");
        Directory.CreateDirectory(oldRoot);
        var stored = Stored(oldRoot);
        TestSupport.Write(Path.Combine(Scanner.DataDirForGame(stored), "disabled", "SomeMod.dll"), "held");

        var proposed = Copy(stored);
        proposed.GameRoot = Path.Combine(TestSupport.TempDir("rc-new-"), "ELDEN RING");
        Directory.CreateDirectory(proposed.GameRoot);
        // Occupy the destination data dir so the move is refused.
        TestSupport.Write(Path.Combine(Scanner.DataDirForGame(proposed), "occupied.txt"), "x");

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.False(plan.CanSave);
        Assert.NotEmpty(plan.Blockers);
    }

    // Changing the engine changes which preset defaults apply, so a field that reads as "untouched"
    // under one engine may read as customised under another — quietly altering whether future
    // manifest corrections reach this game. Report it; do not decide for the user.
    [Fact]
    public void Changing_the_engine_is_noted_because_it_shifts_the_preset_baseline()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.Engine = "ue-pak";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.NotEmpty(plan.Notes);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void Fields_already_marked_stay_marked()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.UserSet = new[] { GameEntry.UserSetFileExtensions };
        var proposed = Copy(stored);
        proposed.GroupingRule = "filename_no_ext";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetFileExtensions, plan.FieldsToPin);
        Assert.Contains(GameEntry.UserSetGroupingRule, plan.FieldsToPin);
    }

    // Over-pinning is as damaging as under-pinning, and invisible. This repo spells the SAME logical
    // mod path both ways — EnginePresets and the manifest use forward slashes ("Content/Paks/~mods"),
    // while ModLocations.UePakModLocation builds it with Path.Combine, which yields backslashes on
    // Windows. If a cosmetic difference reads as "changed", saving pins modLocations, and a pinned
    // field permanently outranks manifest corrections for that game (Scanner.GameContext) — which is
    // exactly the "194 .archive mods showed as zero" failure this spec exists to fix.
    [Fact]
    public void Two_spellings_of_the_same_mod_path_are_not_a_change()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.ModLocations = new[] { new ModLocation("mods", "mods", "Content\\Paks\\~mods") };
        var proposed = Copy(stored);
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", "Content/Paks/~mods") };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Empty(plan.FieldsChanged);
        Assert.DoesNotContain(GameEntry.UserSetModLocations, plan.FieldsToPin);
    }

    // A trailing separator is the same folder too, and extensions differing only by stray whitespace
    // are the same list. Both over-pin the same way if they read as an edit.
    [Fact]
    public void A_trailing_separator_and_padded_extensions_are_not_a_change()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.FileExtensions = new[] { "pak", "ucas" };
        stored.ModLocations = new[] { new ModLocation("mods", "mods", "mod") };
        var proposed = Copy(stored);
        proposed.FileExtensions = new[] { " pak", "ucas " };
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", "mod\\") };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Empty(plan.FieldsChanged);
        Assert.DoesNotContain(GameEntry.UserSetFileExtensions, plan.FieldsToPin);
        Assert.DoesNotContain(GameEntry.UserSetModLocations, plan.FieldsToPin);
    }

    // A pasted or half-typed folder is the likeliest user error a repair surface will ever see, and it
    // is the most expensive one: a blank root makes Scanner.DataDirForGame fall back to ".", which
    // yields a RELATIVE _626mods\<id> that resolves against the launcher's own working directory. The
    // plan would then cheerfully report moving the user's only copy of their disabled mods into the
    // launcher's install folder, with no blocker and CanSave true.
    [Fact]
    public void A_blank_game_folder_is_blocked_rather_than_planned()
    {
        var stored = Stored(Path.Combine(TestSupport.TempDir("rc-"), "ELDEN RING"));
        Directory.CreateDirectory(stored.GameRoot);
        TestSupport.Write(Path.Combine(Scanner.DataDirForGame(stored), "disabled", "SomeMod.dll"), "the only copy");

        var proposed = Copy(stored);
        proposed.GameRoot = "   ";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.False(plan.CanSave);
        Assert.NotEmpty(plan.Blockers);
        Assert.Null(plan.DataDir);   // nothing aimed at the launcher's working directory
    }

    [Fact]
    public void A_game_folder_that_does_not_exist_is_blocked_and_plans_no_move()
    {
        var stored = Stored(Path.Combine(TestSupport.TempDir("rc-"), "ELDEN RING"));
        Directory.CreateDirectory(stored.GameRoot);
        TestSupport.Write(Path.Combine(Scanner.DataDirForGame(stored), "disabled", "SomeMod.dll"), "the only copy");

        var proposed = Copy(stored);
        proposed.GameRoot = Path.Combine(TestSupport.TempDir("rc-new-"), "ELDNE RING");   // never created

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.False(plan.CanSave);
        Assert.NotEmpty(plan.Blockers);
        Assert.Null(plan.DataDir);   // a blocked plan never computes a move
    }

    // A field listed in FieldsToPin becomes userSet on save, and a pinned field permanently outranks
    // manifest corrections for that game (Scanner.GameContext). AddGameDialog already rewrites the
    // mod-path box when the engine dropdown changes, and EnginePresets.BuildGameEntry fills
    // FileExtensions and GroupingRule from the preset whenever the input's are null — so an entry
    // arriving from either path carries the NEW preset's values in three fields the user never typed.
    // Pinning those would opt the game out of every future fix because someone touched a dropdown.
    [Fact]
    public void An_engine_change_does_not_pin_the_new_presets_own_defaults()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var uePak = EnginePresets.Presets["ue-pak"];
        var proposed = Copy(stored);
        proposed.Engine = "ue-pak";
        proposed.FileExtensions = uePak.FileExtensions;
        proposed.GroupingRule = uePak.GroupingRule;
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", uePak.ModPath) };

        var plan = RegistrationChange.Plan(stored, proposed);

        // The values really did change — the plan still says so honestly.
        Assert.Contains(GameEntry.UserSetFileExtensions, plan.FieldsChanged);
        Assert.Contains(GameEntry.UserSetGroupingRule, plan.FieldsChanged);
        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsChanged);

        // But none of them is a stated choice, so none of them gets pinned.
        Assert.Empty(plan.FieldsToPin);
        Assert.True(plan.CanSave);
    }

    // The other half of the same rule: dropping a pin the user actually earned would silently re-expose
    // a deliberate choice to being overwritten by a manifest correction.
    [Fact]
    public void An_engine_change_still_pins_a_value_the_user_really_chose()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var uePak = EnginePresets.Presets["ue-pak"];
        var proposed = Copy(stored);
        proposed.Engine = "ue-pak";
        proposed.FileExtensions = new[] { "smpcmod", "suit" };   // not the preset's, so a real choice
        proposed.GroupingRule = uePak.GroupingRule;
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", uePak.ModPath) };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetFileExtensions, plan.FieldsToPin);
        Assert.DoesNotContain(GameEntry.UserSetGroupingRule, plan.FieldsToPin);
        Assert.DoesNotContain(GameEntry.UserSetModLocations, plan.FieldsToPin);
    }

    // THE NAME IS NOT PART OF THE QUESTION. A registration's first location is named whatever the
    // manifest or ModLocator called it — "~mods", "paks", "loc0" — and only the PATH says whether the
    // folder is the preset's own default. Comparing the name too meant any game whose first location
    // was not literally named "mods" could never be recognised as sitting on the default, so an engine
    // change always pinned modLocations and permanently opted that game out of future mod-path
    // corrections — while the dialog truthfully promised exactly that, which is worse.
    [Fact]
    public void A_preset_default_path_is_recognised_whatever_the_location_is_named()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.ModLocations = new[] { new ModLocation("~mods", "Paks", "mod") };
        var uePak = EnginePresets.Presets["ue-pak"];
        var proposed = Copy(stored);
        proposed.Engine = "ue-pak";
        proposed.FileExtensions = uePak.FileExtensions;
        proposed.GroupingRule = uePak.GroupingRule;
        // Same non-"mods" name the stored entry carries, now pointing at the preset's own folder.
        proposed.ModLocations = new[] { new ModLocation("~mods", "Paks", uePak.ModPath) };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsChanged);   // it really did change
        Assert.Empty(plan.FieldsToPin);                                      // but the preset said it
    }

    // Changing a game folder does NOT rewrite ModEngineConfig (an absolute path that ModEngine2Listing
    // gates on File.Exists) or a LaunchTarget whose exe Target is already rooted — so a corrected
    // FromSoft folder can silently drop out of the Mod Engine 2 lane and leave Launch firing an
    // executable in the old location. Re-running detection would fix those AND overwrite ModLocations,
    // clobbering the mod folder the user just typed. So the planner says it instead.
    [Fact]
    public void Changing_the_game_folder_says_it_does_not_re_check_how_the_game_launches()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.GameRoot = TestSupport.TempDir("rc-new-");

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(plan.Notes, n => n.Contains("Re-detect", StringComparison.Ordinal));
        Assert.True(plan.CanSave);
    }

    // The other half: an edit that leaves the folder alone must not carry the advice. A note on every
    // save is a note nobody reads by the third one.
    [Fact]
    public void An_edit_that_leaves_the_game_folder_alone_carries_no_launch_note()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.DoesNotContain(plan.Notes, n => n.Contains("Re-detect", StringComparison.Ordinal));
    }

    [Fact]
    public void Planning_writes_nothing()
    {
        var root = TestSupport.TempDir("rc-");
        var stored = Stored(Path.Combine(root, "ELDEN RING"));
        Directory.CreateDirectory(stored.GameRoot);
        var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        RegistrationChange.Plan(stored, Copy(stored));

        Assert.Equal(before, Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray());
    }

    // The unchanged case above never reaches DataDirMove.Plan at all, so on its own it proves nothing
    // about the one branch that delegates outward. This is the riskier path: a real move plan gets
    // built, over a populated data dir, and STILL nothing may be written until the user says so.
    [Fact]
    public void Planning_a_game_folder_change_writes_nothing_either()
    {
        var root = TestSupport.TempDir("rc-");
        var stored = Stored(Path.Combine(root, "a", "ELDEN RING"));
        Directory.CreateDirectory(stored.GameRoot);
        TestSupport.Write(Path.Combine(Scanner.DataDirForGame(stored), "disabled", "SomeMod.dll"), "held file");

        var proposed = Copy(stored);
        proposed.GameRoot = Path.Combine(root, "b", "ELDEN RING");
        Directory.CreateDirectory(proposed.GameRoot);

        var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.NotNull(plan.DataDir);   // the delegation really happened; the assertion below has teeth
        Assert.Equal(before, Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray());
    }

    // ---- changes that are real but carry no pin ----

    // FieldsChanged is deliberately the four PINNABLE fields. Without a second list, renaming a game
    // saves a real change while the consequences panel sits blank — the exact lie spec 1's final
    // review warned about.
    [Fact]
    public void A_rename_is_reported_as_an_other_change_and_pins_nothing()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring (Reforged)";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.FieldGameName, plan.OtherChanges);
        Assert.Empty(plan.FieldsChanged);
        Assert.Empty(plan.FieldsToPin);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void Steam_id_and_required_launcher_are_other_changes()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.SteamAppId = "1245620";
        proposed.RequiredLauncher = "ersc_launcher.exe";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.FieldSteamAppId, plan.OtherChanges);
        Assert.Contains(GameEntry.FieldRequiredLauncher, plan.OtherChanges);
        Assert.Empty(plan.FieldsChanged);
    }

    // A pinnable field belongs in FieldsChanged and must NOT be duplicated into OtherChanges —
    // a UI rendering both lists would show it twice and imply two separate consequences.
    [Fact]
    public void A_pinnable_change_is_never_duplicated_into_other_changes()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", "Game/mod") };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsChanged);
        Assert.DoesNotContain(GameEntry.UserSetModLocations, plan.OtherChanges);
    }

    [Fact]
    public void An_engine_change_is_an_other_change_as_well_as_a_note()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.Engine = "ue-pak";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.FieldEngine, plan.OtherChanges);
        Assert.NotEmpty(plan.Notes);
    }

    [Fact]
    public void An_unchanged_entry_reports_no_other_changes()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));

        Assert.Empty(RegistrationChange.Plan(stored, Copy(stored)).OtherChanges);
    }

    // A6. Declining the move pins the registration to where the data ALREADY is. The rule is one word
    // — stored, not proposed — and getting it wrong writes a dataDir that points at an empty folder,
    // so the launcher stops finding this game's disabled mods while the real copy sits unreferenced.
    [Fact]
    public void Declining_the_move_pins_the_data_dir_to_where_the_data_already_is()
    {
        var oldRoot = Path.Combine(TestSupport.TempDir("rc-old-"), "ELDEN RING");
        Directory.CreateDirectory(oldRoot);
        var stored = Stored(oldRoot);
        var held = Path.Combine(Scanner.DataDirForGame(stored), "disabled", "SomeMod.dll");
        TestSupport.Write(held, "held file");

        var proposed = Copy(stored);
        proposed.GameRoot = Path.Combine(TestSupport.TempDir("rc-new-"), "ELDEN RING");
        Directory.CreateDirectory(proposed.GameRoot);

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Equal(Scanner.DataDirForGame(stored), plan.PinDataDirTo);
        Assert.NotEqual(Scanner.DataDirForGame(proposed), plan.PinDataDirTo);

        // And the point of it: once pinned, the corrected registration finds the held file.
        proposed.DataDir = plan.PinDataDirTo;
        Assert.True(File.Exists(Path.Combine(Scanner.DataDirForGame(proposed), "disabled", "SomeMod.dll")));
    }

    [Fact]
    public void There_is_nothing_to_pin_when_there_is_no_move()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring";

        Assert.Null(RegistrationChange.Plan(stored, proposed).PinDataDirTo);
    }

    // A6's reshape hazard. Plan compares location lists by count first, and is RIGHT to: a list that
    // really grew or shrank is a real edit. This test says so out loud, because it is the reason
    // callers must not rebuild the list themselves — a rebuilt list is read as a stated choice and
    // pinned, opting the game out of every future mod-path correction.
    [Fact]
    public void A_reshaped_location_list_reads_as_a_change_which_is_why_callers_edit_in_place()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.ModLocations = ThreeLocations();
        var proposed = Copy(stored);
        proposed.ModLocations = new[] { stored.ModLocations[0] };   // 3 → 1, same first path

        Assert.Contains(GameEntry.UserSetModLocations, RegistrationChange.Plan(stored, proposed).FieldsToPin);
    }

    [Fact]
    public void Editing_one_location_of_three_keeps_all_three()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.ModLocations = ThreeLocations();
        var proposed = Copy(stored);

        // A rename, with the mod-path box left as it was: nothing about the locations changed.
        proposed.GameName = "Elden Ring";
        proposed.ModLocations = RegistrationChange.EditLocation(stored.ModLocations, 0, stored.ModLocations[0].Path);
        var unchanged = RegistrationChange.Plan(stored, proposed);

        Assert.Equal(3, proposed.ModLocations.Count);
        Assert.DoesNotContain(GameEntry.UserSetModLocations, unchanged.FieldsChanged);
        Assert.DoesNotContain(GameEntry.UserSetModLocations, unchanged.FieldsToPin);

        // A real correction to the first path: one location changes, the other two ride along.
        proposed.ModLocations = RegistrationChange.EditLocation(stored.ModLocations, 0, "  Game/mod  ");
        var corrected = RegistrationChange.Plan(stored, proposed);

        Assert.Equal(3, proposed.ModLocations.Count);
        Assert.Equal(new ModLocation("mods", "mods", "Game/mod"), proposed.ModLocations[0]);
        Assert.Equal(stored.ModLocations.Skip(1), proposed.ModLocations.Skip(1));
        Assert.Contains(GameEntry.UserSetModLocations, corrected.FieldsToPin);
    }

    // The setup dialog only edits the first location today. A user whose SECOND location is wrong had
    // no repair path at all; the rule itself is index-agnostic so the dialog can grow one.
    [Fact]
    public void The_second_location_can_be_corrected_without_touching_the_first()
    {
        var stored = ThreeLocations();

        var edited = RegistrationChange.EditLocation(stored, 1, "Game/mod2-fixed");

        Assert.Equal(stored[0], edited[0]);
        Assert.Equal(new ModLocation("mods2", "mods2", "Game/mod2-fixed"), edited[1]);
        Assert.Equal(stored[2], edited[2]);
    }

    [Fact]
    public void No_stored_location_and_a_blank_path_proposes_no_location_and_pins_nothing()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.ModLocations = Array.Empty<ModLocation>();
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring";

        proposed.ModLocations = RegistrationChange.EditLocation(stored.ModLocations, 0, "   ");
        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Empty(proposed.ModLocations);   // 0 → 0, not 0 → 1 with an empty path at the game root
        Assert.DoesNotContain(GameEntry.UserSetModLocations, plan.FieldsToPin);
    }

    [Fact]
    public void No_stored_location_and_a_typed_path_is_a_stated_choice_and_is_pinned()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.ModLocations = Array.Empty<ModLocation>();
        var proposed = Copy(stored);

        proposed.ModLocations = RegistrationChange.EditLocation(stored.ModLocations, 0, "mods");
        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Single(proposed.ModLocations);
        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsToPin);
    }

    [Fact]
    public void Editing_a_location_that_does_not_exist_is_refused_rather_than_invented()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RegistrationChange.EditLocation(ThreeLocations(), 3, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegistrationChange.EditLocation(ThreeLocations(), -1, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegistrationChange.EditLocation(Array.Empty<ModLocation>(), 1, "x"));
    }

    // What ModLocator.Detect produces for a game with several mod folders on disk.
    private static ModLocation[] ThreeLocations() => new[]
    {
        new ModLocation("mods", "mods", "mod"),
        new ModLocation("mods2", "mods2", "Game/mod2"),
        new ModLocation("mods3", "mods3", "Game/mod3"),
    };

    // A blank mod path resolves to the game root, so clearing the box would make the whole install the
    // mod folder and pin it. "." is how a root-level mod folder is spelled; blank is only a cleared box.
    [Fact]
    public void Clearing_an_existing_mod_folder_is_blocked_rather_than_pointed_at_the_game_root()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);

        proposed.ModLocations = RegistrationChange.EditLocation(stored.ModLocations, 0, "   ");
        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.False(plan.CanSave);
        Assert.Contains(plan.Blockers, b => b.Contains("can't be blank"));
    }

    [Fact]
    public void A_mod_folder_of_dot_is_the_game_folder_and_is_allowed()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);

        proposed.ModLocations = RegistrationChange.EditLocation(stored.ModLocations, 0, ".");

        Assert.True(RegistrationChange.Plan(stored, proposed).CanSave);
    }

    // The old dialog could save a blank path. A registration already carrying one must not become
    // uneditable: an unrelated rename still saves, as it did before.
    [Fact]
    public void A_registration_already_carrying_a_blank_path_can_still_be_renamed()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.ModLocations = new[] { new ModLocation("mods", "mods", "") };
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring";
        proposed.ModLocations = RegistrationChange.EditLocation(stored.ModLocations, 0, "");

        Assert.True(RegistrationChange.Plan(stored, proposed).CanSave);
    }
}
