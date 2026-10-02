using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Frameworks;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 2 review of "Return to vanilla holds every active mod": each Important finding was reproduced by a
/// probe, and each probe is a regression test here. Plus the reversibility-audit and minor findings: no
/// partial file on a final path, no rooted manifest path, the pre-flight counting extra trees, schema 1 when
/// nothing needs 2, an interrupted newer-schema point never offered for discard, a failed manifest rewrite
/// that says so, per-lane sheet wording, stranded refusals retried, and a held name Windows can't copy.
/// </summary>
public class SafeClearReviewRegressionTests : IDisposable
{
    private const string Ts = "20261002-120000";
    private static readonly string[] Trees = { "r6/scripts", "r6/tweaks", "bin/x64/plugins/cyber_engine_tweaks/mods" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-review-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string DataRoot => Path.Combine(_root, "appdata");
    private string RpDir => Path.Combine(DataRoot, "restore-points", Ts);

    private sealed class FakeNexus : INexusGate { public bool IsConnected => true; public void DeleteStoredKey() { } }
    private sealed class FakeProbe : IGameRunningProbe { public bool AnyRunning(GameEntry g) => false; }
    private sealed class Provider(IEnumerable<GameEntry> games) : IGameProvider
    {
        private readonly List<GameEntry> _games = games.ToList();
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g)
            => g.Id == "tree" ? Scanner.GameContext(g, extraModTrees: Trees) : Scanner.GameContext(g);
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

    private RestorePointOrchestrator Make(Provider p)
        => new(DataRoot, Path.Combine(DataRoot, "restore-points"), "0.5.0", p, new FakeNexus(), new FakeProbe());

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

    private static void RegisterFramework(string dataDir, string id, string installPath, params string[] files)
    {
        var fwDir = Path.Combine(dataDir, "frameworks", id);
        Directory.CreateDirectory(fwDir);
        var manifest = new FrameworkInstallManifest(id, id, "Someone", installPath, files, DateTime.UtcNow, null);
        File.WriteAllText(Path.Combine(fwDir, "install.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static bool HoldsAnyFile(string dir)
        => Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();

    private GameEntry PakGame()
    {
        var root = Path.Combine(_root, "pakgame");
        Put(Path.Combine(root, "mods", "alpha.pak"), "ALPHA");
        Put(Path.Combine(root, "mods", "beta.pak"), "BETA");
        return new GameEntry
        {
            Id = "pak", GameName = "Pak Game", GameRoot = root, DataDir = DataDir("pak"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
    }

    private GameEntry TreeGame()
    {
        var root = Path.Combine(_root, "treegame");
        Put(Path.Combine(root, "archive", "pc", "mod", "CoolMod.archive"), "MAIN-v1");
        Put(Path.Combine(root, "r6", "scripts", "CoolMod", "main.reds"), "SCRIPTS-v1");
        Put(Path.Combine(root, "r6", "tweaks", "CoolMod.yaml"), "TWEAK-v1");
        return new GameEntry
        {
            Id = "tree", GameName = "Tree Game", Engine = "custom", GameRoot = root, DataDir = DataDir("tree"),
            FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
    }

    // ---- I1: a loader a framework installed belongs to the framework, not to the turn-offs ----

    [Fact]
    public async Task I1_a_framework_owned_DLL_loader_under_Game_comes_back_as_one_loader_row_with_nothing_held()
    {
        var root = Path.Combine(_root, "er");
        var play = Path.Combine(root, "Game");
        Put(Path.Combine(play, "eldenring.exe"), "EXE");
        Put(Path.Combine(play, "dinput8.dll"), "ELM-DLL");
        Put(Path.Combine(play, "mod_loader_config.ini"), "ELM-INI");
        Put(Path.Combine(play, "mods", "Foo.dll"), "FOO");
        var g = new GameEntry { Id = "er", GameName = "ER", Engine = "fromsoft", GameRoot = root, DataDir = DataDir("er") };
        RegisterFramework(g.DataDir!, "elden-mod-loader", play, "dinput8.dll", "mod_loader_config.ini");
        var before = Snapshot(root);
        var p = new Provider(new[] { g });
        var orch = Make(p);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var set = RestorePointManifestStore.Read(RpDir)!.Games[0].TurnedOffByClear!;
        Assert.DoesNotContain(set, x => x.Name == DirectInject.LoaderName);   // the framework owns it

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        AssertSameTree(before, root);
        var rows = ModListing.Resolve(g);
        var loader = Assert.Single(rows, m => m.IsLoader);
        Assert.True(loader.Enabled);
        Assert.All(rows, m => Assert.True(m.Enabled, m.Name));
        Assert.False(HoldsAnyFile(DirectInjectListing.Holding(g)));
        Assert.False(HoldsAnyFile(DirectInject.VanillaProxyHolding(play)));
    }

    [Fact]
    public async Task I1_a_framework_owned_proxy_loader_comes_back_as_one_file_with_nothing_stepped_aside()
    {
        var g = PakGame();
        Put(Path.Combine(g.GameRoot, "version.dll"), "PROXY");
        RegisterFramework(g.DataDir!, "some-loader", g.GameRoot, "version.dll");
        var before = Snapshot(g.GameRoot);
        var p = new Provider(new[] { g });
        var orch = Make(p);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Assert.DoesNotContain(RestorePointManifestStore.Read(RpDir)!.Games[0].TurnedOffByClear!,
            x => x.Location == ProxyLoaderRows.LocationTag);

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        AssertSameTree(before, g.GameRoot);
        Assert.False(HoldsAnyFile(DirectInject.VanillaProxyHolding(g.GameRoot)));
        Assert.Single(ModListing.Resolve(g), m => m.Location == ProxyLoaderRows.LocationTag && m.Enabled);
    }

    // ---- I2: a mod the user already turned back on is not put back into holding ----

    [Fact]
    public async Task I2_a_mod_turned_back_on_by_hand_before_restore_stays_live_with_nothing_held()
    {
        var g = PakGame();
        var p = new Provider(new[] { g });
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        await Scanner.EnableModAsync("alpha", p.ContextFor(g));   // the user turns it back on by hand

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Empty(restore.Warnings);
        Assert.Equal("ALPHA", File.ReadAllText(Path.Combine(g.GameRoot, "mods", "alpha.pak")));
        Assert.False(Directory.Exists(Path.Combine(p.ContextFor(g).DisabledRoot, "alpha")));   // no duplicate hold
        Assert.Single(ModListing.Resolve(g), m => m.Name == "alpha" && m.Enabled);
        // And it turns off again cleanly, rather than refusing on a phantom held copy.
        await ModToggle.SetEnabledAsync(p.ContextFor(g), ModListing.Resolve(g).First(m => m.Name == "alpha"), false);
        Assert.False(File.Exists(Path.Combine(g.GameRoot, "mods", "alpha.pak")));
    }

    // ---- I3: never merge a different data-folder copy with the archived one ----

    [Fact]
    public async Task I3_a_different_copy_in_the_data_folder_is_left_as_it_is_and_reported()
    {
        var g = TreeGame();
        var p = new Provider(new[] { g });
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ctx = p.ContextFor(g);
        File.WriteAllText(Path.Combine(ctx.DisabledRoot, "CoolMod", "CoolMod.archive"), "MAIN-v2");
        Directory.Delete(Path.Combine(ctx.DataDir, "disabled-trees"), true);

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Contains(restore.Warnings, w => w.Contains("CoolMod")
            && w.Contains("the data folder holds a different copy than the restore point, so 626 left it as it is"));
        // Nothing written: the v2 hold is as the user left it, no tree entries were put back, the game is vanilla.
        Assert.Equal("MAIN-v2", File.ReadAllText(Path.Combine(ctx.DisabledRoot, "CoolMod", "CoolMod.archive")));
        Assert.False(Directory.Exists(Path.Combine(ctx.DataDir, "disabled-trees", "CoolMod")));
        Assert.False(File.Exists(Path.Combine(g.GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
        Assert.False(File.Exists(Path.Combine(g.GameRoot, "r6", "scripts", "CoolMod", "main.reds")));
    }

    // ---- Reversibility audit: no partial file on a final path ----

    [Fact]
    public async Task A_put_back_that_fails_midway_leaves_no_partial_file_in_the_data_folder()
    {
        var g = TreeGame();
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ctx = p.ContextFor(g);
        Directory.Delete(ctx.DisabledRoot, true);
        Directory.Delete(Path.Combine(ctx.DataDir, "disabled-trees"), true);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        var calls = 0;

        RestorePointEngine.BeforeHeldPutBackFileForTests = _ => { if (++calls == 2) throw new IOException("injected: disk went away"); };
        ReplayResult r;
        try { r = RestorePointEngine.ReplayGame(ga, Path.Combine(RpDir, "games", ga.Id), ctx); }
        finally { RestorePointEngine.BeforeHeldPutBackFileForTests = null; }

        Assert.Contains(r.NotBackOn, s => s.Name == "CoolMod");
        Assert.False(HoldsAnyFile(Path.Combine(ctx.DataDir, "disabled", "CoolMod")));
        Assert.False(HoldsAnyFile(Path.Combine(ctx.DataDir, "disabled-trees", "CoolMod")));
        Assert.Empty(Directory.Exists(ctx.DataDir)
            ? Directory.GetFiles(ctx.DataDir, "*.rp-tmp", SearchOption.AllDirectories) : Array.Empty<string>());
    }

    // ---- M1: a rooted path in a manifest is refused at every restore site ----

    [Theory]
    [InlineData(@"\evil.bin")]
    [InlineData("/evil.bin")]
    public async Task M1_a_rooted_moved_file_path_is_refused(string rel)
    {
        var g = PakGame();
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0] with { MovedFiles = new[] { new MovedFile(rel, 1, null) } };

        Assert.Throws<InvalidOperationException>(() =>
            RestorePointEngine.ReplayGame(ga, Path.Combine(RpDir, "games", ga.Id), p.ContextFor(g)));
    }

    [Fact]
    public async Task M1_a_rooted_framework_file_path_is_refused()
    {
        var g = PakGame();
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var dir = Path.Combine(RpDir, "games", "pak");
        Put(Path.Combine(dir, "frameworks-state", "fw", "evil.dll"), "X");
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0] with
        {
            Frameworks = new[] { new FrameworkArchive("fw", "fw", "a", g.GameRoot, new[] { @"\evil.dll" }, Path.Combine("frameworks-state", "fw")) },
        };

        Assert.Throws<InvalidOperationException>(() => RestorePointEngine.ReplayGame(ga, dir, p.ContextFor(g)));
    }

    [Fact]
    public async Task M1_a_rooted_held_copy_path_refuses_that_mod_and_writes_nothing()
    {
        var g = PakGame();
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ctx = p.ContextFor(g);
        Directory.Delete(ctx.DisabledRoot, true);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        ga = ga with
        {
            HeldCopies = ga.HeldCopies!.Select(h => h.Name != "alpha" ? h
                : h with { Files = h.Files.Select(f => f with { Rel = @"\" + f.Rel }).ToList() }).ToList(),
        };

        var r = RestorePointEngine.ReplayGame(ga, Path.Combine(RpDir, "games", "pak"), ctx);

        Assert.Contains(r.NotBackOn, s => s.Name == "alpha");
        Assert.False(File.Exists(Path.Combine(g.GameRoot, "mods", "alpha.pak")));
        Assert.Equal("BETA", File.ReadAllText(Path.Combine(g.GameRoot, "mods", "beta.pak")));   // the other one came back
    }

    // ---- M2: the pre-flight counts extra-tree entries ----

    [Fact]
    public void M2_the_turn_off_estimate_counts_extra_tree_entries()
    {
        var g = TreeGame();
        var c = Scanner.GameContext(g, extraModTrees: Trees);
        var expected = "MAIN-v1".Length + "SCRIPTS-v1".Length + "TWEAK-v1".Length;

        Assert.Equal(expected, RestorePointEngine.EstimateTurnOffBytes(c));
    }

    // ---- M3: schema 2 only when a game carries the turn-off record ----

    [Fact]
    public async Task M3_a_mods_active_only_clear_writes_schema_1_and_a_vanilla_clear_schema_2()
    {
        var g = PakGame();
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "modsActive" }, Ts, default)).Ok);
        Assert.Equal(1, RestorePointManifestStore.Read(RpDir)!.SchemaVersion);

        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, "20261002-130000", default)).Ok);
        Assert.Equal(2, RestorePointManifestStore.Read(Path.Combine(DataRoot, "restore-points", "20261002-130000"))!.SchemaVersion);
    }

    // ---- M4: an interrupted point from a newer build is not "unsealed" ----

    [Fact]
    public void M4_an_interrupted_newer_schema_point_reads_as_sealed_by_a_newer_626_and_is_never_discarded()
    {
        Directory.CreateDirectory(RpDir);
        File.WriteAllText(Path.Combine(DataRoot, "safe-clear.lock"), Ts);
        RestorePointManifestStore.WriteSealed(RpDir, new RestorePointManifest(99, "9.0.0", Ts, true, true, 0, 0,
            Array.Empty<GameArchive>()));
        var orch = Make(new Provider(Array.Empty<GameEntry>()));

        var ic = orch.DetectInterruptedClear();

        Assert.NotNull(ic);
        Assert.True(ic!.Sealed);
        Assert.True(ic.NewerSchema);
        orch.DiscardPartial(Ts);
        Assert.True(File.Exists(Path.Combine(RpDir, RestorePointManifestStore.FileName)));   // never discarded
        Assert.Contains("newer", RestorePointOrchestrator.InterruptedNewerMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("update", RestorePointOrchestrator.InterruptedNewerMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ---- M5: a failed manifest rewrite after the held copy says so ----

    [Fact]
    public async Task M5_a_failed_rewrite_after_the_held_copy_warns()
    {
        var g = PakGame();
        var orch = Make(new Provider(new[] { g }));
        orch.BeforeManifestRewriteForTests = () => throw new IOException("injected: manifest locked");

        var r = await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default);

        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("restore point") && w.Contains("injected: manifest locked"));
        Assert.Null(RestorePointManifestStore.Read(RpDir)!.Games[0].HeldCopies);   // the seal stands, undescribed copies aside
    }

    // ---- M6: the sheet says where each lane's mods went, and copies only for what was copied ----

    [Fact]
    public void M6_the_sheet_names_each_lanes_place_and_copies_only_what_was_copied()
    {
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla", Array.Empty<LaunchTarget>(), null,
            Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null,
            TurnedOffByClear: new[]
            {
                new ClearedMod("A", "mods"), new ClearedMod("B", "mods"),
                new ClearedMod("version.dll", ProxyLoaderRows.LocationTag), new ClearedMod("Me2Mod", "mod engine 2"),
            },
            DataDir: @"D:\_626mods\t",
            HeldCopies: new[] { new HeldCopy("A", new[] { new MovedFile(@"disabled\A\A.pak", 1, "x") }) });

        var s = OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, "rp"));

        Assert.Contains("turned off 4 mods", s);
        Assert.Contains(@"D:\_626mods\t", s);
        Assert.Contains("2 held in its data folder", s);
        Assert.Contains(@"_626\vanilla-proxy", s);
        Assert.Contains("Mod Engine 2", s);
        Assert.Contains("A copy of 1 of them is saved in your restore point", s);
        Assert.Contains("turns exactly those 4 back on", s);
        Assert.DoesNotContain("don't delete", s, StringComparison.OrdinalIgnoreCase);
    }

    // ---- M7: a refused turn-off whose entries were stranded in holding is retried on restore ----

    [Fact]
    public void M7_a_refused_turn_off_stranded_in_holding_is_turned_back_on_and_reported()
    {
        var g = TreeGame();
        Put(Path.Combine(g.GameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod", "init.lua"), "CET");
        var before = Snapshot(g.GameRoot);
        var c = Scanner.GameContext(g, extraModTrees: Trees);
        var dir = Path.Combine(_root, "archive", "games", "tree");
        var ga = RestorePointEngine.CaptureGame(new GameCaptureInput(g, c, "vanilla"), dir);
        var moves = RestorePointEngine.PlanVanillaMoves(c);
        var set = RestorePointEngine.PlanVanillaTurnOffs(c, moves);
        var locked = Path.Combine(g.GameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "CoolMod", "init.lua");
        var heldScripts = Path.Combine(c.DataDir, "disabled-trees", "CoolMod", "r6", "scripts", "CoolMod");

        EndStateResult end;
        Scanner.BeforeRollbackMoveForTests = from =>
        {
            if (string.Equals(from, heldScripts, StringComparison.OrdinalIgnoreCase)) throw new IOException("injected: cannot move back");
        };
        try
        {
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                end = RestorePointEngine.ApplyEndState(c, "vanilla", dir, moves, set);
        }
        finally { Scanner.BeforeRollbackMoveForTests = null; }
        Assert.Contains(end.TurnOffSkips, s => s.Name == "CoolMod");
        Assert.Contains(ModListing.Resolve(g), m => m.Name == "CoolMod" && !m.Enabled);   // stranded: listed off

        var r = RestorePointEngine.ReplayGame(ga with { MovedFiles = moves, TurnedOffByClear = set, TurnOffSkipped = end.TurnOffSkips }, dir, c);

        Assert.Empty(r.NotBackOn);
        Assert.Contains(r.Recovered, s => s.Name == "CoolMod");
        AssertSameTree(before, g.GameRoot);
    }

    // ---- M8: a held name Windows can't copy fails closed, and restore uses the data folder ----

    [Fact]
    public async Task M8_a_held_file_whose_name_ends_in_a_dot_fails_the_copy_closed_and_restore_uses_the_data_folder()
    {
        var root = Path.Combine(_root, "dotgame");
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "Cool"));
        File.WriteAllText(Path.Combine(mods, "Cool", "data.bin"), "DATA");
        File.WriteAllText(@"\\?\" + Path.Combine(mods, "Cool", "readme."), "DOT");   // only reachable by its exact name
        var g = new GameEntry
        {
            Id = "dot", GameName = "Dot Game", GameRoot = root, DataDir = DataDir("dot"),
            FileExtensions = new[] { "bin" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods") { Form = "folders" } },
        };
        var p = new Provider(new[] { g });
        var orch = Make(p);
        Assert.Contains(ModListing.Resolve(g), m => m.Name == "Cool" && m.Enabled);

        var r = await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default);

        Assert.True(r.Ok);
        Assert.Contains(r.Warnings, w => w.Contains("Dot Game") && w.Contains("couldn't be copied"));
        Assert.Null(RestorePointManifestStore.Read(RpDir)!.Games[0].HeldCopies);
        Assert.False(Directory.Exists(Path.Combine(RpDir, "games", "dot", RestorePointEngine.HeldDirName)));

        var restore = await orch.RestoreAsync(Ts, default);

        // Restore goes to the data folder's hold, the only copy, and nothing else. The scanner's own turn-on
        // can't yet move a file reachable only by its exact name (a plain disable/enable round trip fails the
        // same way, outside Safe Clear), so the mod is reported rather than claimed, and its hold is intact.
        Assert.True(restore.Ok);
        Assert.Contains(restore.Warnings, w => w.Contains("\"Cool\" is not back on"));
        var held = Path.Combine(p.ContextFor(g).DisabledRoot, "Cool", "Cool");
        Assert.Equal("DATA", File.ReadAllText(Path.Combine(held, "data.bin")));
        Assert.Equal("DOT", File.ReadAllText(@"\\?\" + Path.Combine(held, "readme.")));
    }
}
