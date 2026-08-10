using System.Reflection;
using ModManager.Core;

namespace ModManager.Tests;

// A registration editor has to hand the planner a WHOLE entry — the fields the user typed plus every
// field they did not. The obvious way to build one is an object initialiser naming all 27 properties,
// which is correct exactly once: the 28th property added later is silently dropped from every edit, so
// renaming a game would quietly clear its nexusGameDomain or its autoBackupOnLaunch, with no compiler
// error and no failing test. This branch already caught that bug once, in a test helper.
//
// CloneShallow is MemberwiseClone, so a new field carries itself. These tests hold that line against
// someone "clarifying" it into a hand-written initialiser later — the reflection sweep below has no
// property list of its own to fall out of date.
public class GameEntryCloneTests
{
    // Every settable property, given a value that is not the type's default, so a field the clone
    // failed to carry shows up as a difference rather than two matching nulls.
    private static GameEntry Populated() => new()
    {
        Id = "elden-ring",
        GameName = "ELDEN RING",
        Engine = "fromsoft",
        WindowTitle = "ELDEN RING™",
        GameRoot = Path.Combine("C:", "games", "EldenRing"),
        FileExtensions = new[] { "dll", "ini" },
        GroupingRule = "by_folder",
        ModLocations = new[] { new ModLocation("mods", "Mods", "mod") },
        SteamAppId = "1245620",
        LaunchUrl = "steam://rungameid/1245620",
        LaunchExe = "Game/eldenring.exe",
        LaunchTargets = new[] { new LaunchTarget("Mod Engine 2", "exe", "Game/launchmod.bat") },
        ModEngineConfig = Path.Combine("C:", "games", "EldenRing", "Game", "config.toml"),
        DataDir = Path.Combine("C:", "data", "elden-ring"),
        CurseforgeGameId = 4242,
        ScanSubfolders = "always",
        SaveDir = Path.Combine("C:", "saves", "elden-ring"),
        RequiredLauncher = "Game/launch_elden_ring_seamlesscoop.exe",
        SaveModPath = "RocksDB/{version}/Worlds",
        SaveModForbidden = new[] { "RocksDB_v2" },
        NexusGameDomain = "eldenring",
        AutoBackupOnLaunch = true,
        SaveAutoKeep = 7,
        LastKnownSteamBuildId = "18752634",
        StoreSource = "steam",
        LastLaunchedUtc = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc),
        UserSet = new[] { GameEntry.UserSetModLocations },
    };

    // The whole point: no property list in this test either. Reflection walks whatever GameEntry has
    // TODAY, so a field added tomorrow is covered the moment it exists.
    [Fact]
    public void CloneShallow_carries_every_settable_property()
    {
        var original = Populated();

        var clone = original.CloneShallow();

        var props = typeof(GameEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToList();
        Assert.NotEmpty(props);
        foreach (var p in props)
            Assert.Equal(p.GetValue(original), p.GetValue(clone));
    }

    // Guards against the value the populated fixture cannot: a property left at its default on the
    // original must not be the reason the sweep above passes. If any settable property were still
    // holding its default here, a clone that dropped it would look identical.
    [Fact]
    public void The_fixture_leaves_no_settable_property_at_its_default()
    {
        var original = Populated();
        var blank = new GameEntry();

        var same = typeof(GameEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => Equals(p.GetValue(original), p.GetValue(blank)))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(same);
    }

    // A clone that shared state with its source would make an editor's "propose this" quietly rewrite
    // the stored entry it is being compared against — and the comparison is the whole planner.
    [Fact]
    public void Editing_the_clone_leaves_the_original_alone()
    {
        var original = Populated();

        var clone = original.CloneShallow();
        clone.GameName = "Renamed";
        clone.GameRoot = Path.Combine("D:", "games", "EldenRing");
        clone.ModLocations = new[] { new ModLocation("mods", "Mods", "mods") };
        clone.UserSet = new[] { GameEntry.UserSetGameRoot };

        Assert.Equal("ELDEN RING", original.GameName);
        Assert.Equal(Path.Combine("C:", "games", "EldenRing"), original.GameRoot);
        Assert.Equal("mod", original.ModLocations[0].Path);
        Assert.Equal(new[] { GameEntry.UserSetModLocations }, original.UserSet);
    }
}
