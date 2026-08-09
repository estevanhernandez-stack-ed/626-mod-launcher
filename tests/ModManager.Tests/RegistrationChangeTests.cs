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
}
