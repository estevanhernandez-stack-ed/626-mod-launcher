using System.Diagnostics;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 11 (/code-review on #385):
/// <list type="bullet">
/// <item>A game at a drive root (<c>G:\</c>): the containment helpers appended a separator to a root that
/// already ends in one, so nothing was ever "inside" the game. One helper now adds exactly one.</item>
/// <item>An interrupted reset from a newer 626 can be acknowledged once: only the lock goes, the restore point
/// stays.</item>
/// </list>
/// </summary>
public class SafeClearRound11Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-r11-" + Guid.NewGuid().ToString("n"));
    private string? _subst;

    public void Dispose()
    {
        if (_subst is not null) RunSubst($"{_subst} /D");
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void Put(string abs, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    // ---- 1. Containment with a root that already ends in a separator ----

    [Fact]
    public void A_path_under_a_filesystem_root_is_under_it()
    {
        var fsRoot = Path.GetPathRoot(Path.GetTempPath())!;   // "C:\" on Windows, "/" elsewhere: ends in a separator
        Assert.True(RealPath.IsStrictlyUnder(Path.Combine(fsRoot, "mods"), fsRoot));
        Assert.True(RealPath.IsStrictlyUnder(Path.Combine(fsRoot, "a", "b"), fsRoot));
        Assert.False(RealPath.IsStrictlyUnder(fsRoot, fsRoot));   // the root itself is not under itself
        Assert.Equal(fsRoot, RealPath.WithTrailingSeparator(fsRoot));
    }

    [WindowsFact]
    public void Drive_root_shaped_strings_are_handled_with_exactly_one_separator()
    {
        Assert.True(RealPath.IsStrictlyUnder(@"X:\mods", @"X:\"));
        Assert.True(RealPath.IsStrictlyUnder(@"X:\mods\a.pak", @"X:\mods"));
        Assert.True(RealPath.IsStrictlyUnder(@"X:\mods\a.pak", @"X:\mods\"));
        Assert.False(RealPath.IsStrictlyUnder(@"X:\modsextra", @"X:\mods"));
        Assert.False(RealPath.IsStrictlyUnder(@"X:\", @"X:\"));
        Assert.Equal(@"X:\", RealPath.WithTrailingSeparator(@"X:\"));
        Assert.Equal(@"X:\mods\", RealPath.WithTrailingSeparator(@"X:\mods"));
        Assert.Equal("mods", ModOnlyFolders.RelativeToRoot(@"X:\", @"X:\mods"));
        Assert.Equal("mods/sub", ModOnlyFolders.RelativeToRoot(@"X:\", @"X:\mods\sub"));
        Assert.Null(ModOnlyFolders.RelativeToRoot(@"X:\", @"X:\"));
        Assert.Null(ModOnlyFolders.RelativeToRoot(@"X:\", @"Y:\mods"));
    }

    private static int RunSubst(string args)
    {
        var psi = new ProcessStartInfo("subst.exe", args)
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit(10000);
        return p.ExitCode;
    }

    private string? FreeDriveFor(string target)
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        foreach (var letter in "QRSTUVWXYZ")
        {
            if (used.Contains(letter)) continue;
            var drive = letter + ":";
            if (RunSubst($"{drive} \"{target}\"") == 0 && Directory.Exists(drive + @"\")) { _subst = drive; return drive + @"\"; }
        }
        return null;
    }

    [WindowsFact]
    public void A_game_at_a_real_drive_root_has_its_mod_folder_inside_it_and_its_mods_turn_off()
    {
        var backing = Path.Combine(_root, "drive");
        Put(Path.Combine(backing, "game.exe"), "EXE");
        Put(Path.Combine(backing, "mods", "alpha.pak"), "ALPHA");
        var driveRoot = FreeDriveFor(backing);
        Assert.True(driveRoot is not null, "subst failed: no free drive letter");
        var dataDir = Path.Combine(_root, "data");
        Directory.CreateDirectory(dataDir);
        var g = new GameEntry
        {
            Id = "dr", GameName = "Drive Root Game", Engine = "minecraft", GameRoot = driveRoot, DataDir = dataDir,
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var c = Scanner.GameContext(g);

        Assert.Equal("mods", ModOnlyFolders.RelativeToRoot(c.GameRoot, c.Locations[0].Abs));
        Assert.NotNull(ModOnlyFolders.WhyModOnly(c, c.Locations[0]));
        Assert.Equal(new[] { "alpha" }, RestorePointEngine.PlanVanillaTurnOffs(c, Array.Empty<MovedFile>()).Select(m => m.Name));
        var plan = RestorePointEngine.PlanVanillaRemainder(c, Array.Empty<ClearSkip>());
        Assert.DoesNotContain(plan.LeftInPlace, n => n.Reason.Contains("can't tell"));
    }

    [WindowsFact]
    public void When_the_game_is_a_drive_root_a_location_equal_to_it_is_the_game_not_an_ancestor_or_a_whole_drive()
    {
        var backing = Path.Combine(_root, "drive2");
        Put(Path.Combine(backing, "game.exe"), "EXE");
        var driveRoot = FreeDriveFor(backing);
        Assert.True(driveRoot is not null, "subst failed: no free drive letter");
        var dataDir = Path.Combine(_root, "data2");
        Directory.CreateDirectory(dataDir);
        var c = Scanner.GameContext(new GameEntry { Id = "dr2", GameName = "DR2", GameRoot = driveRoot, DataDir = dataDir });

        Assert.Null(RestorePointEngine.SystemFolderReason(c, driveRoot!));
    }

    // A subst drive resolves (GetFinalPathNameByHandle) to its backing folder, so the two tests above prove the
    // drive-root path end to end but can't reproduce the bug. The system drive's root is a REAL drive root;
    // these read it without writing anything.
    [WindowsFact]
    public void On_the_real_system_drive_root_a_folder_below_it_is_inside_it()
    {
        var sysRoot = WindowsOnly.SystemDriveRoot()!;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.Equal(Path.GetFileName(windows), ModOnlyFolders.RelativeToRoot(sysRoot, windows), StringComparer.OrdinalIgnoreCase);
        Assert.True(RealPath.IsAtOrUnder(windows, sysRoot));
    }

    [WindowsFact]
    public void When_the_game_is_the_real_drive_root_a_location_equal_to_it_is_the_game_itself()
    {
        var sysRoot = WindowsOnly.SystemDriveRoot()!;
        var dataDir = Path.Combine(_root, "data3");
        Directory.CreateDirectory(dataDir);
        var c = Scanner.GameContext(new GameEntry { Id = "c", GameName = "C", GameRoot = sysRoot, DataDir = dataDir });

        Assert.Null(RestorePointEngine.SystemFolderReason(c, sysRoot));   // the game, not "a whole drive"
    }

    [Fact]
    public void The_ancestor_check_still_names_a_real_ancestor()
    {
        var common = Path.Combine(_root, "common");
        var game = Path.Combine(common, "TheGame");
        Directory.CreateDirectory(game);
        var c = Scanner.GameContext(new GameEntry { Id = "a", GameName = "A", GameRoot = game, DataDir = Path.Combine(_root, "d") });
        Assert.Equal("it contains the game folder itself", RestorePointEngine.SystemFolderReason(c, common));
        Assert.Null(RestorePointEngine.SystemFolderReason(c, game));
    }

    // ---- 2. Acknowledging an interrupted reset from a newer 626 ----

    private sealed class NoGames : IGameProvider
    {
        public IReadOnlyList<GameEntry> Games => Array.Empty<GameEntry>();
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        public void Reload() { }
    }
    private sealed class FakeNexus : INexusGate { public bool IsConnected => true; public void DeleteStoredKey() { } }
    private sealed class FakeProbe : IGameRunningProbe { public bool AnyRunning(GameEntry g) => false; }

    [Fact]
    public void Acknowledging_a_newer_interrupted_reset_removes_only_the_lock_and_keeps_the_restore_point()
    {
        const string ts = "20261003-140000";
        var dataRoot = Path.Combine(_root, "appdata");
        var rpRoot = Path.Combine(dataRoot, "restore-points");
        var rpDir = Path.Combine(rpRoot, ts);
        Directory.CreateDirectory(rpDir);
        File.WriteAllText(Path.Combine(dataRoot, SafeClearLock.FileName), ts);
        RestorePointManifestStore.WriteSealed(rpDir, new RestorePointManifest(99, "9.0.0", ts, true, true, 0, 0, Array.Empty<GameArchive>()));
        Put(Path.Combine(rpDir, "games", "g", "held", "x.pak"), "HELD");
        var orch = new RestorePointOrchestrator(dataRoot, rpRoot, "0.5.0", new NoGames(), new FakeNexus(), new FakeProbe());
        Assert.True(orch.DetectInterruptedClear() is { NewerSchema: true });

        orch.AcknowledgeInterruptedClear(ts);

        Assert.False(File.Exists(Path.Combine(dataRoot, SafeClearLock.FileName)));   // no more reminders
        Assert.Null(orch.DetectInterruptedClear());
        Assert.True(File.Exists(Path.Combine(rpDir, RestorePointManifestStore.FileName)));   // the restore point stays
        Assert.Equal("HELD", File.ReadAllText(Path.Combine(rpDir, "games", "g", "held", "x.pak")));
    }

    [Fact]
    public void Acknowledging_leaves_a_lock_that_names_a_different_reset()
    {
        var dataRoot = Path.Combine(_root, "appdata2");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, SafeClearLock.FileName), "20261003-150000");

        Assert.False(SafeClearLock.Acknowledge(dataRoot, "20261003-140000"));
        Assert.True(File.Exists(Path.Combine(dataRoot, SafeClearLock.FileName)));
        Assert.True(SafeClearLock.Acknowledge(dataRoot, "20261003-150000"));
        Assert.False(File.Exists(Path.Combine(dataRoot, SafeClearLock.FileName)));
    }

    [Fact]
    public void The_acknowledge_copy_says_the_restore_point_stays()
    {
        Assert.Contains("restore point stays", RestorePointOrchestrator.InterruptedNewerAcknowledge, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".", RestorePointOrchestrator.InterruptedNewerAcknowledge);
        Assert.DoesNotContain(";", RestorePointOrchestrator.InterruptedNewerMessage);
    }
}
