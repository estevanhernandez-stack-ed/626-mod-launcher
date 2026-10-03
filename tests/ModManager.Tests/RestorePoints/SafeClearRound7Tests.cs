using System.Diagnostics;
using System.Security.Cryptography;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 7 (review r6). The principle for the whole PR: vouch by ALLOWLIST or by PROOF OF OWNERSHIP, never by
/// a list of exclusions. Outside the game folder a row turns off only when an install record proves 626
/// placed it, so a mis-set location turns nothing off through ANY alias: a junction, the <c>\\?\</c> prefix,
/// UNC to the parent, <c>C:\Users</c>, SysWOW64, OneDrive, Steam. Containment is compared on real paths.
/// Also: the PakClassifier widening is reverted (a project-named pak is a mod), and the minors.
///
/// <para>The fixture root is OUTSIDE the user profile (<c>C:\626-r7-…</c>), so the profile cases exercise the
/// rule itself and not the ancestor shortcut a %TEMP% game root would hit (review r6 test gap).</para>
/// </summary>
public class SafeClearRound7Tests : IDisposable
{
    private const string Ts = "20261002-220000";
    private readonly string _root;
    private readonly List<string> _links = new();

    public SafeClearRound7Tests()
    {
        // Outside the profile: the system drive's root, on Windows when it can be written. Everywhere else
        // (Linux, a locked-down root) %TEMP%: the tests that need the outside-the-profile root skip there
        // (WindowsFact / WindowsTheory with NeedsWritableSystemDriveRoot), and nothing ever builds a relative
        // drive-letter folder under the working directory.
        string? rooted = null;
        if (WindowsOnly.SystemDriveRoot() is { } sysRoot && WindowsOnly.SystemDriveRootWritable)
        {
            var candidate = Path.Combine(sysRoot, "626-r7-" + Guid.NewGuid().ToString("n")[..10]);
            try { Directory.CreateDirectory(candidate); rooted = candidate; } catch { }
        }
        _root = rooted ?? Path.Combine(Path.GetTempPath(), "rp-r7-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var l in _links) try { if (Directory.Exists(l)) Directory.Delete(l); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private bool OutsideProfile
        => !RealPath.IsAtOrUnder(_root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private string DataRoot => Path.Combine(_root, "appdata");
    private string RpDir => Path.Combine(DataRoot, "restore-points", Ts);

    private sealed class FakeNexus : INexusGate { public bool IsConnected => true; public void DeleteStoredKey() { } }
    private sealed class FakeProbe : IGameRunningProbe { public bool AnyRunning(GameEntry g) => false; }
    private sealed class Provider(IEnumerable<GameEntry> games) : IGameProvider
    {
        private readonly List<GameEntry> _games = games.ToList();
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        public void Reload() { }
    }

    private static void Put(string abs, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    private string DataDir(string id)
    {
        var d = Path.Combine(DataRoot, "_626mods", id);
        Directory.CreateDirectory(d);
        return d;
    }

    private RestorePointOrchestrator Make(GameEntry g)
        => new(DataRoot, Path.Combine(DataRoot, "restore-points"), "0.5.0", new Provider(new[] { g }), new FakeNexus(), new FakeProbe());

    private static Dictionary<string, string> Snapshot(string root)
        => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(root, p),
                p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.OrdinalIgnoreCase);

    private static void AssertSameTree(Dictionary<string, string> expected, string root)
    {
        var actual = Snapshot(root);
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
            actual.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        foreach (var (rel, sha) in expected) Assert.Equal(sha, actual[rel]);
    }

    private bool Junction(string link, string target)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            if (Directory.Exists(link)) { _links.Add(link); return true; }
            return false;
        }
        catch { return false; }
    }

    // A Steam-library shape: steamapps/common holds TheGame and OtherGame.
    private (string Common, string GameRoot) Library()
    {
        var common = Path.Combine(_root, "lib", "steamapps", "common");
        var root = Path.Combine(common, "TheGame");
        Put(Path.Combine(root, "game.exe"), "EXE");
        Put(Path.Combine(root, "Data", "base.pak"), "BASE");
        Put(Path.Combine(common, "OtherGame", "other.exe"), "OTHER");
        return (common, root);
    }

    private GameEntry GameWithLocation(string id, string gameRoot, string location, string form = "folders", params string[] exts) => new()
    {
        Id = id, GameName = id, Engine = "custom", GameRoot = gameRoot, DataDir = DataDir(id),
        FileExtensions = exts.Length > 0 ? exts : new[] { "pak" },
        ModLocations = new[] { new ModLocation("mods", "Mods", location) { Form = form } },
    };

    private async Task AssertClearTurnsNothingOffAndChangesNothing(GameEntry g, string watch)
    {
        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
        var before = Snapshot(watch);
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        AssertSameTree(before, watch);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Empty(ga.TurnedOffByClear!);
        Assert.Empty(ga.VanillaRemainder!);
        Assert.False(OffBoardingHydrator.FullyVanilla(ga));
    }

    // ---- I-1: aliases of an ancestor, real clears ----

    [WindowsFact(NeedsWritableSystemDriveRoot = true)]
    public void The_fixture_root_is_outside_the_user_profile()
        => Assert.True(OutsideProfile, $"{_root} is inside the profile; the profile cases would not exercise the rule");

    [WindowsFact]
    public async Task A_location_that_is_a_junction_to_the_games_parent_turns_nothing_off()
    {
        var (common, root) = Library();
        var link = Path.Combine(_root, "lnk-common");
        Assert.True(Junction(link, common), "mklink /J failed");
        await AssertClearTurnsNothingOffAndChangesNothing(GameWithLocation("j1", root, link), common);
    }

    [WindowsFact]
    public async Task A_game_registered_through_a_junction_with_the_real_parent_as_location_turns_nothing_off()
    {
        var (common, root) = Library();
        var linkRoot = Path.Combine(_root, "lnk-game");
        Assert.True(Junction(linkRoot, root), "mklink /J failed");
        await AssertClearTurnsNothingOffAndChangesNothing(GameWithLocation("j1b", linkRoot, common), common);
    }

    [WindowsFact]
    public async Task The_extended_length_spelling_of_the_parent_turns_nothing_off()
    {
        var (common, root) = Library();
        await AssertClearTurnsNothingOffAndChangesNothing(GameWithLocation("j1f", root, @"\\?\" + common), common);
    }

    [WindowsFact]
    public async Task A_UNC_path_to_the_parent_turns_nothing_off()
    {
        var (common, root) = Library();
        if (Path.GetPathRoot(common) is not { Length: > 0 } driveRoot) return;
        var drive = driveRoot.TrimEnd('\\', ':');
        var unc = $@"\\localhost\{drive}$\{common[3..]}";
        if (!Directory.Exists(unc)) return;   // admin shares off on this machine: the junction cases stand in
        await AssertClearTurnsNothingOffAndChangesNothing(GameWithLocation("j3", root, unc), common);
    }

    [WindowsFact]
    public void Real_paths_see_through_a_junction_and_the_extended_prefix()
    {
        var (common, _) = Library();
        var link = Path.Combine(_root, "lnk-common2");
        Assert.True(Junction(link, common), "mklink /J failed");
        Assert.Equal(RealPath.Final(common), RealPath.Final(link), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(RealPath.Final(common), RealPath.Final(@"\\?\" + common), StringComparer.OrdinalIgnoreCase);
        Assert.True(RealPath.IsAtOrUnder(Path.Combine(link, "TheGame"), common));
    }

    // ---- I-1: system-shaped locations, plan only (never run against real folders) ----

    public static IEnumerable<object[]> SystemShapes()
    {
        // Data discovery runs on every host: off Windows there is nothing to yield, and nothing may throw.
        if (WindowsOnly.SystemDriveRoot() is not { } sysRootPath) yield break;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) yield break;
        var sysDrive = sysRootPath.TrimEnd('\\', ':');
        yield return new object[] { "C:\\Users", Path.GetDirectoryName(profile)!, "folders", "dat" };
        yield return new object[] { "profile", profile, "folders", "dat" };
        yield return new object[] { "profile via \\\\?\\", @"\\?\" + profile, "folders", "dat" };
        yield return new object[] { "profile via UNC", $@"\\localhost\{sysDrive}$\{profile[3..]}", "folders", "dat" };
        yield return new object[] { "AppData", Path.Combine(profile, "AppData"), "folders", "dat" };
        yield return new object[] { "SysWOW64", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"), "files", "dll" };
        var oneDrive = Path.Combine(profile, "OneDrive");
        if (Directory.Exists(oneDrive)) yield return new object[] { "OneDrive", oneDrive, "folders", "dat" };
        var steam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(steam)) yield return new object[] { "Steam", steam, "folders", "dll" };
        yield return new object[] { "Steam files", Directory.Exists(steam) ? steam : profile, "files", "dll" };
    }

    [WindowsTheory(NeedsWritableSystemDriveRoot = true)]
    [MemberData(nameof(SystemShapes))]
    public void A_system_shaped_location_plans_zero_turn_offs_and_no_sweep(string what, string location, string form, string ext)
    {
        Assert.True(OutsideProfile, $"fixture inside the profile; {what} would pass via the ancestor shortcut");
        var root = Path.Combine(_root, "g-" + Math.Abs(what.GetHashCode()));
        Put(Path.Combine(root, "game.exe"), "EXE");
        var g = GameWithLocation("sys" + Math.Abs(what.GetHashCode()), root, location, form, ext);
        var c = Scanner.GameContext(g);
        if (!Directory.Exists(c.Locations[0].Abs)) return;   // UNC unavailable here: the junction cases stand in

        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(c, Array.Empty<MovedFile>()));
        Assert.Empty(RestorePointEngine.PlanVanillaRemainder(c, Array.Empty<ClearSkip>()).Files);
    }

    [WindowsFact]
    public void A_drive_relative_location_is_ignored_with_a_note()
    {
        var root = Path.Combine(_root, "dr");
        Put(Path.Combine(root, "game.exe"), "EXE");
        var g = GameWithLocation("dr", root, "C:", "files", "sys");
        var c = Scanner.GameContext(g);

        Assert.True(RealPath.IsDriveRelative("C:"));
        Assert.True(RealPath.IsDriveRelative("C:mods"));
        Assert.False(RealPath.IsDriveRelative(@"C:\mods"));
        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(c, Array.Empty<MovedFile>()));
        var plan = RestorePointEngine.PlanVanillaRemainder(c, Array.Empty<ClearSkip>());
        Assert.Empty(plan.Files);
        Assert.Contains(plan.LeftInPlace, n => n.Reason.Contains("names a drive but no folder"));
    }

    [Fact]
    public async Task Outside_the_game_a_recorded_folder_mod_turns_off_and_its_unrecorded_neighbour_stays()
    {
        var docs = Path.Combine(_root, "Docs", "Game", "Mods");
        var root = Path.Combine(_root, "game-docs");
        Put(Path.Combine(root, "game.exe"), "EXE");
        Put(Path.Combine(docs, "Mine", "a.cfg"), "MINE");
        Put(Path.Combine(docs, "Theirs", "b.cfg"), "THEIRS");
        var g = GameWithLocation("docs", root, docs);
        g.GroupingRule = "by_folder";
        ModInstallRegistry.Save(g.DataDir!, new ModInstallManifest("mine", "mine.zip", "mods", new[] { "Mine/a.cfg" }, DateTime.UtcNow.AddMinutes(1)));
        var before = Snapshot(docs);
        var orch = Make(g);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.False(Directory.Exists(Path.Combine(docs, "Mine")));
        Assert.Equal("THEIRS", File.ReadAllText(Path.Combine(docs, "Theirs", "b.cfg")));
        Assert.True((await orch.RestoreAsync(Ts, default)).Ok);
        AssertSameTree(before, docs);
    }

    // ---- I-2: the PakClassifier widening is reverted ----

    [Theory]
    [InlineData("~mods")]
    [InlineData("paks-root")]
    public void A_project_named_mod_pak_stays_a_visible_toggleable_mod(string shape)
    {
        var root = Path.Combine(_root, "ue-" + shape.Replace("~", ""));
        var paks = Path.Combine(root, "Game", "Content", "Paks");
        var folder = shape == "~mods" ? Path.Combine(paks, "~mods") : paks;
        Put(Path.Combine(folder, "BetterHUD-WindowsNoEditor.pak"), "HUD");
        var g = new GameEntry
        {
            Id = "ue" + shape.Replace("~", ""), GameName = "UE", Engine = "ue-pak", GameRoot = root, DataDir = DataDir("ue" + shape.Replace("~", "")),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[]
            {
                shape == "~mods"
                    ? new ModLocation("mods", "Mods", "Game/Content/Paks/~mods")
                    : new ModLocation("mods", "Paks", "Game/Content/Paks") { Form = "paks-root" },
            },
        };

        Assert.False(PakClassifier.IsBaseGamePak("BetterHUD-WindowsNoEditor.pak", 3));
        Assert.Contains(ModListing.Resolve(g), m => m.Name == "BetterHUD-WindowsNoEditor" && m.Enabled && !m.ReadOnly);
        Assert.Contains(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()),
            m => m.Name == "BetterHUD-WindowsNoEditor");
    }

    // ---- Minors ----

    [Fact]
    public void m_1_the_kept_copy_note_is_true_for_an_update_and_a_game_file_alike()
    {
        Assert.Contains("kept an earlier copy of a file here", RestorePointEngine.ReplacedGameFileNote);
        Assert.DoesNotContain("replaced a game file", RestorePointEngine.ReplacedGameFileNote);
    }

    [Fact]
    public async Task m_2_a_replaced_file_still_matches_after_the_game_folder_moved_to_another_library()
    {
        var root = Path.Combine(_root, "lib1", "Skyrim");
        var data = Path.Combine(root, "Data");
        Put(Path.Combine(data, "Skyrim.esm"), "MASTER");
        Put(Path.Combine(data, "Update.esm"), "ORIGINAL");
        var g = new GameEntry
        {
            Id = "skm", GameName = "Skyrim", Engine = "bethesda", GameRoot = root, DataDir = DataDir("skm"),
            FileExtensions = new[] { "esp", "esm" }, ModLocations = new[] { new ModLocation("mods", "Mods", "Data") },
        };
        var drop = Path.Combine(_root, "drop");
        Put(Path.Combine(drop, "Update.esm"), "CLEANED");
        var c = Scanner.GameContext(g);
        Scanner.ExecuteIntake(Scanner.PlanIntake(new[] { Path.Combine(drop, "Update.esm") }, c), new HashSet<string> { "Update.esm" }, c);

        var moved = Path.Combine(_root, "lib2", "Skyrim");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(root, moved);
        g.GameRoot = moved;

        Assert.DoesNotContain(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()), m => m.Name == "Update");
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Assert.Equal("CLEANED", File.ReadAllText(Path.Combine(moved, "Data", "Update.esm")));
    }

    [Fact]
    public void m_4_a_flagged_folder_holding_an_executable_is_never_vouched_for()
    {
        var root = Path.Combine(_root, "cp");
        Put(Path.Combine(root, "archive", "pc", "mod", "Cool.archive"), "COOL");
        var g = new GameEntry
        {
            Id = "cyberpunk-2077", GameName = "Cyberpunk 2077", Engine = "custom", SteamAppId = "1091500", GameRoot = root,
            DataDir = DataDir("cp"), FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
        var c = Scanner.GameContext(g);
        Assert.NotNull(ModOnlyFolders.WhyModOnly(c, c.Locations[0]));

        Put(Path.Combine(root, "archive", "pc", "mod", "Cyberpunk2077.exe"), "MZ");
        Assert.Null(ModOnlyFolders.WhyModOnly(c, c.Locations[0]));
    }

    // ---- Sheet: never "all" beside items it couldn't tell apart ----

    private static GameArchive Vanilla(int off, int items) => new(
        "t", "T", @"D:\T", "vanilla", Array.Empty<LaunchTarget>(), null,
        Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
        Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null,
        TurnedOffByClear: Enumerable.Range(0, off).Select(i => new ClearedMod("Off" + i, "mods")).ToList(),
        VanillaRemainder: Array.Empty<MovedFile>(),
        LeftInPlace: Enumerable.Range(0, items).Select(i => new InPlaceNote("Item" + i, RestorePointEngine.CantTellRowPrefix + "Data, where 626 can't tell"))
            .Append(new InPlaceNote("Data", "626 can't tell the game's own files from mods in Data; 1 files no mod claims are still in place")).ToList());

    [Fact]
    public void The_sheet_says_the_mods_it_could_tell_apart_when_items_remain()
    {
        var line = OffBoardingHydrator.WhatHappened(Vanilla(3, 191));
        Assert.StartsWith("626 turned off the 3 mods it could tell were mods", line);
        Assert.Contains("191 items 626 can't tell from the game's own files are still in place", line);
        Assert.DoesNotContain("all 3", line);
    }

    [Fact]
    public void The_sheet_says_all_only_when_nothing_else_was_left()
        => Assert.StartsWith("626 turned off all 3 mods it found", OffBoardingHydrator.WhatHappened(Vanilla(3, 0)));
}
