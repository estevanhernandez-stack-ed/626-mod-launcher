using System.Diagnostics;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 8 (review r7): real paths resolved once per LOCATION (m-1); an install record proves ownership of
/// the folder it was written for, by path as well as name (m-2); a path that doesn't exist yet resolves through
/// its existing ancestor (m-3); and the sheet never lists a still-active mod under a "turned off" heading.
/// Windows-only cases use <see cref="WindowsFactAttribute"/> and skip elsewhere.
/// </summary>
public class SafeClearRound8Tests : IDisposable
{
    private const string Ts = "20261003-100000";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-r8-" + Guid.NewGuid().ToString("n"));
    private readonly List<string> _links = new();

    public void Dispose()
    {
        foreach (var l in _links) try { if (Directory.Exists(l)) Directory.Delete(l); } catch { }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

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

    private GameEntry Skyrim(string id, int plugins, string? root = null)
    {
        root ??= Path.Combine(_root, id);
        Put(Path.Combine(root, "Data", "Skyrim.esm"), "MASTER");
        for (var i = 0; i < plugins; i++) Put(Path.Combine(root, "Data", $"Plugin{i:0000}.esp"), "P" + i);
        return new GameEntry
        {
            Id = id, GameName = id, Engine = "bethesda", GameRoot = root, DataDir = DataDir(id),
            FileExtensions = new[] { "esp", "esm" }, ModLocations = new[] { new ModLocation("mods", "Mods", "Data") },
        };
    }

    // ---- m-1: real paths once per location, not per row ----

    private static int RealPathCallsToPlan(GameEntry g)
    {
        var c = Scanner.GameContext(g);
        RealPath.CallsForTests = 0;
        RestorePointEngine.PlanVanillaTurnOffs(c, Array.Empty<MovedFile>());
        return RealPath.CallsForTests;
    }

    [Fact]
    public void Planning_resolves_real_paths_per_location_so_20_rows_and_400_cost_the_same()
    {
        var small = RealPathCallsToPlan(Skyrim("sk20", 20));
        var large = RealPathCallsToPlan(Skyrim("sk400", 400));

        Assert.Equal(small, large);
        Assert.True(small < 40, $"{small} real-path resolutions for one location");
    }

    // ---- m-2: a record proves ownership of the folder it was written for ----

    private static void Record(GameEntry g, string? locationPath, params string[] files)
        => ModInstallRegistry.Save(g.DataDir!, new ModInstallManifest("r" + Guid.NewGuid().ToString("n")[..6], "r.zip", "mods", files,
            DateTime.UtcNow.AddMinutes(1), locationPath));

    [Fact]
    public void A_record_for_the_folder_as_it_is_now_proves_ownership()
    {
        var g = Skyrim("own-match", 1);
        Record(g, "Data", "Plugin0000.esp");
        Assert.Equal(new[] { "Plugin0000" }, RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()).Select(m => m.Name));
    }

    [Fact]
    public void A_record_written_for_another_folder_under_the_same_location_name_proves_nothing()
    {
        var g = Skyrim("own-moved", 1);
        Record(g, "Documents/Old Mods", "Plugin0000.esp");   // "mods" pointed somewhere else when 626 installed
        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
    }

    [Fact]
    public void A_legacy_record_without_a_location_path_keeps_the_name_rule_inside_the_game()
    {
        var g = Skyrim("own-legacy", 1);
        Record(g, null, "Plugin0000.esp");
        Assert.Single(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
    }

    [WindowsFact]
    public void A_legacy_record_is_never_honoured_for_a_location_in_the_Windows_folder()
    {
        // Plan only: never run against System32. The review's O5c: a location mis-set to System32 plus a
        // record naming version.dll planned turning off System32's own version.dll.
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!File.Exists(Path.Combine(system, "version.dll"))) return;
        var root = Path.Combine(_root, "sysgame");
        Put(Path.Combine(root, "game.exe"), "EXE");
        var g = new GameEntry
        {
            Id = "sys32", GameName = "Sys32", Engine = "custom", GameRoot = root, DataDir = DataDir("sys32"),
            FileExtensions = new[] { "dll" }, ModLocations = new[] { new ModLocation("mods", "Mods", system) },
        };
        ModInstallRegistry.Save(g.DataDir!, new ModInstallManifest("legacy", "x.zip", "mods", new[] { "version.dll" },
            new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
    }

    [Fact]
    public async Task A_real_intake_records_where_it_installed_and_the_record_still_holds_after_the_game_moves()
    {
        var g = Skyrim("own-intake", 0, Path.Combine(_root, "lib1", "Skyrim"));
        var drop = Path.Combine(_root, "drop");
        Put(Path.Combine(drop, "NewMod.esp"), "NEW");
        var c = Scanner.GameContext(g);
        Scanner.ExecuteIntake(Scanner.PlanIntake(new[] { Path.Combine(drop, "NewMod.esp") }, c), new HashSet<string>(), c);

        var record = Assert.Single(ModInstallRegistry.List(g.DataDir!));
        Assert.Equal("Data", record.LocationPath);   // relative to the game root: survives a library move

        var moved = Path.Combine(_root, "lib2", "Skyrim");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        Directory.Move(g.GameRoot, moved);
        g.GameRoot = moved;
        Assert.Equal(new[] { "NewMod" }, RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()).Select(m => m.Name));

        var orch = new RestorePointOrchestrator(DataRoot, Path.Combine(DataRoot, "restore-points"), "0.5.0",
            new Provider(new[] { g }), new FakeNexus(), new FakeProbe());
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Assert.False(File.Exists(Path.Combine(moved, "Data", "NewMod.esp")));
        Assert.Equal("MASTER", File.ReadAllText(Path.Combine(moved, "Data", "Skyrim.esm")));
    }

    [Fact]
    public void The_location_path_round_trips_as_camelCase_and_an_old_record_loads_without_it()
    {
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(new ModInstallManifest("a", "a.zip", "mods", new[] { "a.esp" }, DateTime.UtcNow, "Data"), opts);
        Assert.Contains("\"locationPath\"", json);
        Assert.DoesNotContain("\"LocationPath\"", json);
        Assert.Equal("Data", JsonSerializer.Deserialize<ModInstallManifest>(json, opts)!.LocationPath);

        var old = "{\"installId\":\"a\",\"sourceArchive\":\"a.zip\",\"location\":\"mods\",\"files\":[\"a.esp\"],\"installedUtc\":\"2026-01-01T00:00:00Z\"}";
        var g = Skyrim("own-old", 0);
        Directory.CreateDirectory(Path.Combine(g.DataDir!, "installs"));
        File.WriteAllText(Path.Combine(g.DataDir!, "installs", "a.json"), old);
        var loaded = Assert.Single(ModInstallRegistry.List(g.DataDir!));
        Assert.Null(loaded.LocationPath);
    }

    // ---- m-3: a path that doesn't exist yet resolves through its existing ancestor ----

    private bool Junction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit(10000);
        if (!Directory.Exists(link)) return false;
        _links.Add(link);
        return true;
    }

    [WindowsFact]
    public void A_missing_path_under_a_junction_resolves_through_it()
    {
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(_root, "link");
        Assert.True(Junction(link, real), "mklink /J failed");

        Assert.Equal(Path.Combine(RealPath.Final(real)!, "nope", "deeper"), RealPath.Final(Path.Combine(link, "nope", "deeper")),
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("nope", ModOnlyFolders.RelativeToRoot(link, Path.Combine(real, "nope")), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_path_resolves_to_itself_normalised()
    {
        var missing = Path.Combine(_root, "never", "made");
        Assert.Equal(Path.GetFullPath(missing).TrimEnd(Path.DirectorySeparatorChar), RealPath.Final(missing), StringComparer.OrdinalIgnoreCase);
    }

    // ---- Sheet: nothing still active under a "turned off" heading ----

    [Fact]
    public void The_vanilla_sheet_splits_turned_off_still_active_and_already_off()
    {
        var ga = new GameArchive("t", "T", "/games/T", "vanilla", Array.Empty<LaunchTarget>(), null,
            Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(),
            new[]
            {
                new ArchivedMod("Gone", true, null, null, null),
                new ArchivedMod("StillOn", true, null, null, null),
                new ArchivedMod("WasOff", false, null, null, null),
            },
            null,
            TurnedOffByClear: new[] { new ClearedMod("Gone", "mods") },
            VanillaRemainder: Array.Empty<MovedFile>(),
            LeftInPlace: new[] { new InPlaceNote("StillOn", RestorePointEngine.CantTellRowPrefix + "Data, where 626 can't tell") });

        var s = OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, "rp"));

        Assert.DoesNotContain("KEPT, TURNED OFF", s);
        var off = s.IndexOf("Turned off by 626", StringComparison.Ordinal);
        var on = s.IndexOf("Still active (626 left these on)", StringComparison.Ordinal);
        var was = s.IndexOf("Already off before the reset", StringComparison.Ordinal);
        Assert.True(off >= 0 && on > off && was > on, s);
        Assert.InRange(s.IndexOf("    Gone", StringComparison.Ordinal), off, on);
        Assert.InRange(s.IndexOf("    StillOn", StringComparison.Ordinal), on, was);
        Assert.True(s.IndexOf("    WasOff", StringComparison.Ordinal) > was);
    }

    [Fact]
    public void A_mods_active_sheet_keeps_one_plain_list()
    {
        var ga = new GameArchive("t", "T", "/games/T", "modsActive", Array.Empty<LaunchTarget>(), null,
            Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(), new[] { new ArchivedMod("A", true, null, null, null) }, null);
        var s = OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, "rp"));
        Assert.Contains("WHAT'S STILL INSTALLED", s);
        Assert.DoesNotContain("Turned off by 626", s);
    }
}
