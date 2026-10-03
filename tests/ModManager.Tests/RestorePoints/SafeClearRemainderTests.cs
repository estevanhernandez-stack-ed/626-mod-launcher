using System.Security.Cryptography;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 3: "Return to vanilla" means vanilla. After the per-mod turn-offs, everything still left in the
/// game's MOD-ONLY folders (declared extra trees, mod locations that are not the game root, a play folder or
/// base content, and not another tool's) is moved into the restore point under <c>vanilla-remainder/</c>,
/// recorded with its SHA-256 before anything moves, and put back, verified, before Restore's turn-ons.
///
/// <para>The Cyberpunk fixture uses the real store id (1091500) and no explicit trees, so the listing and the
/// toggles both resolve the manifest's five extra trees the same way (the replica harness finding).</para>
/// </summary>
public class SafeClearRemainderTests : IDisposable
{
    private const string Ts = "20261002-140000";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-rem-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

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

    private static readonly string[] CyberpunkModFolders =
    {
        "archive/pc/mod", "r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods",
    };

    // Two mods with rows, and in every extra tree something no row claims, plus .xl sidecars the archive
    // lane never lists (one whose archive is held under a prefixed name, one an orphan).
    private GameEntry Cyberpunk()
    {
        var root = Path.Combine(_root, "Cyberpunk 2077");
        Put(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"), "MZ");
        Put(Path.Combine(root, "archive", "pc", "content", "basegame.archive"), "BASE");   // base content, not a mod folder
        Put(Path.Combine(root, "archive", "pc", "mod", "CoolMod.archive"), "COOL");
        Put(Path.Combine(root, "archive", "pc", "mod", "#CorpoCat.archive"), "CORPO");
        Put(Path.Combine(root, "archive", "pc", "mod", "CorpoCat.archive.xl"), "CORPO-XL");
        Put(Path.Combine(root, "archive", "pc", "mod", "Orphan.archive.xl"), "ORPHAN-XL");
        Put(Path.Combine(root, "r6", "scripts", "CoolMod", "main.reds"), "COOL-SCRIPTS");
        Put(Path.Combine(root, "r6", "scripts", "LHUD", "lhud.reds"), "LHUD");
        Put(Path.Combine(root, "r6", "scripts", "loose.reds"), "LOOSE");
        Put(Path.Combine(root, "r6", "tweaks", "CorpoCat_Red.yaml"), "TWEAK");
        Put(Path.Combine(root, "r6", "input", "keys.xml"), "INPUT");
        Put(Path.Combine(root, "red4ext", "plugins", "ArchiveXL", "ArchiveXL.dll"), "AXL");
        Put(Path.Combine(root, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "AppearanceMenuMod", "init.lua"), "AMM");
        return new GameEntry
        {
            Id = "cyberpunk-2077", GameName = "Cyberpunk 2077", Engine = "custom", SteamAppId = "1091500",
            GameRoot = root, DataDir = DataDir("cyberpunk-2077"),
            FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
    }

    private static IEnumerable<string> LiveInModFolders(GameEntry g)
        => CyberpunkModFolders.Select(f => Path.Combine(g.GameRoot, Path.Combine(f.Split('/'))))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories));

    [Fact]
    public void The_pre_flight_counts_every_byte_in_the_mod_only_folders_and_nothing_else()
    {
        var g = Cyberpunk();
        var expected = LiveInModFolders(g).Sum(f => new FileInfo(f).Length);

        Assert.Equal(expected, RestorePointEngine.EstimateModOnlyBytes(Scanner.GameContext(g)));
    }

    [Fact]
    public void Fixture_resolves_the_manifests_five_extra_trees_by_store_id()
        => Assert.Equal(5, Scanner.GameContext(Cyberpunk()).ExtraModTrees!.Count);

    [Fact]
    public async Task After_vanilla_nothing_is_live_in_any_mod_only_folder_and_restore_is_byte_identical()
    {
        var g = Cyberpunk();
        var before = Snapshot(g.GameRoot);
        var orch = Make(new Provider(new[] { g }));

        var r = await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.True(r.Warnings.Count == 0, string.Join(" | ", r.Warnings));
        Assert.Empty(LiveInModFolders(g));
        // The game's own files are not mod-only and stay exactly where they are.
        Assert.Equal("MZ", File.ReadAllText(Path.Combine(g.GameRoot, "bin", "x64", "Cyberpunk2077.exe")));
        Assert.Equal("BASE", File.ReadAllText(Path.Combine(g.GameRoot, "archive", "pc", "content", "basegame.archive")));

        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.NotNull(ga.VanillaRemainder);
        Assert.Empty(ga.LeftInPlace!);
        Assert.Contains(ga.VanillaRemainder!, f => f.Rel.Replace('\\', '/') == "archive/pc/mod/Orphan.archive.xl");
        Assert.Contains(ga.VanillaRemainder!, f => f.Rel.Replace('\\', '/') == "red4ext/plugins/ArchiveXL/ArchiveXL.dll");
        foreach (var f in ga.VanillaRemainder!)
            Assert.Equal(f.Sha256, FileTally.Sha256(Path.Combine(RpDir, "games", g.Id, RestorePointEngine.RemainderDirName, f.Rel)), ignoreCase: true);

        var sheet = OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, RpDir));
        Assert.Contains("returned to vanilla", sheet);
        Assert.DoesNotContain("WHAT'S STILL INSTALLED", sheet);

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, g.GameRoot);
        Assert.All(ModListing.Resolve(g), m => Assert.True(m.Enabled, m.Name));
    }

    [Fact]
    public async Task A_UE_game_whose_mod_location_is_the_Paks_root_keeps_its_base_paks_and_says_so()
    {
        var root = Path.Combine(_root, "uegame");
        var paks = Path.Combine(root, "Game", "Content", "Paks");
        Put(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak"), "BASE-PAK");
        Put(Path.Combine(paks, "notes.txt"), "NOT A PAK");
        var g = new GameEntry
        {
            Id = "ue", GameName = "UE Game", GameRoot = root, DataDir = DataDir("ue"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Paks", "Game/Content/Paks") { Form = "paks-root" } },
        };
        Assert.True((await Make(new Provider(new[] { g })).SafeClearAsync(
            new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.Equal("BASE-PAK", File.ReadAllText(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak")));
        Assert.Equal("NOT A PAK", File.ReadAllText(Path.Combine(paks, "notes.txt")));
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Empty(ga.VanillaRemainder!);
        Assert.Contains(ga.LeftInPlace!, n => n.Reason.Contains("can't tell the game's own files from mods"));
        Assert.DoesNotContain("returned to vanilla", OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, RpDir)));
    }

    [Fact]
    public async Task An_owned_folder_is_untouched_and_named_as_still_in_place()
    {
        var g = Cyberpunk();
        var owned = Path.Combine(g.GameRoot, "vortexmods");
        Put(Path.Combine(owned, "vortex.deployment.archive.json"), "{}");
        Put(Path.Combine(owned, "loose.dat"), "OWNED");
        g.ModLocations = g.ModLocations.Append(new ModLocation("vortex", "Vortex", "vortexmods")).ToArray();
        Assert.True((await Make(new Provider(new[] { g })).SafeClearAsync(
            new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.Equal("OWNED", File.ReadAllText(Path.Combine(owned, "loose.dat")));
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.DoesNotContain(ga.VanillaRemainder!, f => f.Rel.StartsWith("vortexmods", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(ga.LeftInPlace!, n => n.Path.Contains("vortexmods") && n.Reason.Contains("Vortex"));
        var sheet = OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, RpDir));
        Assert.Contains("STILL IN PLACE", sheet);
        Assert.Contains("vortexmods", sheet);
        Assert.DoesNotContain("returned to vanilla", sheet);
    }

    [Fact]
    public void A_crash_between_remainder_moves_leaves_each_file_live_or_archived_and_restore_recovers()
    {
        var g = Cyberpunk();
        var before = Snapshot(g.GameRoot);
        var c = Scanner.GameContext(g);
        var dir = Path.Combine(_root, "archive", "games", g.Id);
        var ga = RestorePointEngine.CaptureGame(new GameCaptureInput(g, c, "vanilla"), dir);
        var moves = RestorePointEngine.PlanVanillaMoves(c);
        var set = RestorePointEngine.PlanVanillaTurnOffs(c, moves);
        RestorePointEngine.ApplyEndState(c, "vanilla", dir, moves, set);
        var plan = RestorePointEngine.PlanVanillaRemainder(c, Array.Empty<ClearSkip>());
        Assert.True(plan.Files.Count >= 4);

        var n = 0;
        RestorePointEngine.BeforeRemainderMoveForTests = _ => { if (++n == 3) throw new SimulatedCrash(); };
        try { Assert.Throws<SimulatedCrash>(() => RestorePointEngine.SweepRemainder(c, plan.Files, dir)); }
        finally { RestorePointEngine.BeforeRemainderMoveForTests = null; }

        // Every planned file is in exactly one known place: still live, or in the archive under its record.
        foreach (var f in plan.Files)
        {
            var live = File.Exists(Path.Combine(g.GameRoot, f.Rel));
            var archived = File.Exists(Path.Combine(dir, RestorePointEngine.RemainderDirName, f.Rel));
            Assert.True(live || archived, f.Rel);
        }

        var r = RestorePointEngine.ReplayGame(
            ga with { MovedFiles = moves, TurnedOffByClear = set, VanillaRemainder = plan.Files, LeftInPlace = plan.LeftInPlace },
            dir, c);

        Assert.Empty(r.NotBackOn);
        Assert.Empty(r.RemainderIssues);
        AssertSameTree(before, g.GameRoot);
    }

    private sealed class SimulatedCrash : Exception { }

    [Fact]
    public async Task Restore_refuses_to_overwrite_a_different_live_file_and_reports_it()
    {
        var g = Cyberpunk();
        var orch = Make(new Provider(new[] { g }));
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var loose = Path.Combine(g.GameRoot, "r6", "scripts", "loose.reds");
        Put(loose, "THE USER'S NEW FILE");

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Equal("THE USER'S NEW FILE", File.ReadAllText(loose));
        Assert.Contains(restore.Warnings, w => w.Contains("loose.reds") && w.Contains("different"));
        // Everything else came back.
        Assert.Equal("LHUD", File.ReadAllText(Path.Combine(g.GameRoot, "r6", "scripts", "LHUD", "lhud.reds")));
        Assert.Equal("COOL", File.ReadAllText(Path.Combine(g.GameRoot, "archive", "pc", "mod", "CoolMod.archive")));
    }

    [Fact]
    public async Task A_rooted_remainder_path_is_refused()
    {
        var g = Cyberpunk();
        var orch = Make(new Provider(new[] { g }));
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        ga = ga with { VanillaRemainder = new[] { new MovedFile(@"\evil.dll", 1, "00") } };

        var r = RestorePointEngine.ReplayGame(ga, Path.Combine(RpDir, "games", g.Id), Scanner.GameContext(g));

        Assert.Contains(r.RemainderIssues, i => i.Name == @"\evil.dll");
    }

    [Fact]
    public void Remainder_record_round_trips_as_camelCase()
    {
        Directory.CreateDirectory(RpDir);
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla", Array.Empty<LaunchTarget>(), null,
            Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null,
            VanillaRemainder: new[] { new MovedFile("r6/scripts/loose.reds", 5, "AB") },
            LeftInPlace: new[] { new InPlaceNote("vortexmods", "managed by Vortex") });
        RestorePointManifestStore.WriteSealed(RpDir, new RestorePointManifest(2, "x", Ts, true, true, 0, 0, new[] { ga }));
        var json = File.ReadAllText(Path.Combine(RpDir, RestorePointManifestStore.FileName));

        Assert.Contains("\"vanillaRemainder\"", json);
        Assert.Contains("\"leftInPlace\"", json);
        Assert.Contains("\"path\"", json);
        Assert.Contains("\"reason\"", json);
        Assert.DoesNotContain("\"VanillaRemainder\"", json);
        Assert.DoesNotContain("\"LeftInPlace\"", json);
        Assert.DoesNotContain("\"Path\"", json);

        var back = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Equal("r6/scripts/loose.reds", Assert.Single(back.VanillaRemainder!).Rel);
        Assert.Equal("managed by Vortex", Assert.Single(back.LeftInPlace!).Reason);
    }

    // ---- Re-review minors ----

    [Fact]
    public async Task A_mod_live_under_another_location_is_reported_as_already_on_a_different_copy()
    {
        var root = Path.Combine(_root, "twoloc");
        Put(Path.Combine(root, "mods", "alpha.pak"), "ALPHA");
        Directory.CreateDirectory(Path.Combine(root, "mods2"));
        var g = new GameEntry
        {
            Id = "two", GameName = "Two", Engine = "minecraft", GameRoot = root, DataDir = DataDir("two"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods"), new ModLocation("mods2", "Mods 2", "mods2") },
        };
        var p = new Provider(new[] { g });
        var orch = Make(p);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Directory.Delete(p.ContextFor(g).DisabledRoot, true);                 // the data folder lost its hold
        Put(Path.Combine(root, "mods2", "alpha.pak"), "ANOTHER ALPHA");        // and the user put a different one in

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Contains(restore.Warnings, w => w.Contains("alpha") && w.Contains("already on (a different copy)"));
        Assert.False(Directory.Exists(Path.Combine(p.ContextFor(g).DisabledRoot, "alpha")));   // nothing put into holding
        Assert.Equal("ANOTHER ALPHA", File.ReadAllText(Path.Combine(root, "mods2", "alpha.pak")));
    }

    [Fact]
    public async Task A_put_back_rollback_never_deletes_a_file_it_did_not_write()
    {
        var root = Path.Combine(_root, "pak");
        Put(Path.Combine(root, "mods", "alpha.pak"), "ALPHA");
        var g = new GameEntry
        {
            Id = "pak", GameName = "Pak", Engine = "minecraft", GameRoot = root, DataDir = DataDir("pak"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var c = p.ContextFor(g);
        Directory.Delete(c.DisabledRoot, true);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        string? planted = null;

        // Something else's file lands on the destination after the missing-check: the move onto it fails,
        // and the rollback must leave that file alone.
        RestorePointEngine.BeforeHeldPutBackFileForTests = dest =>
        {
            if (planted is null) { planted = dest; Put(dest, "SOMEONE ELSE'S"); }
        };
        ReplayResult r;
        try { r = RestorePointEngine.ReplayGame(ga, Path.Combine(RpDir, "games", g.Id), c); }
        finally { RestorePointEngine.BeforeHeldPutBackFileForTests = null; }

        Assert.Contains(r.NotBackOn, s => s.Name == "alpha");
        Assert.Equal("SOMEONE ELSE'S", File.ReadAllText(planted!));
    }

    [Fact]
    public void The_sheet_says_it_for_one_held_mod()
    {
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla", Array.Empty<LaunchTarget>(), null,
            Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null,
            TurnedOffByClear: new[] { new ClearedMod("A", "mods") },
            DataDir: @"D:\_626mods\t",
            HeldCopies: new[] { new HeldCopy("A", new[] { new MovedFile(@"disabled\A\A.pak", 1, "x") }) });

        var s = OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, "rp"));

        Assert.Contains("A copy of it is saved in your restore point", s);
        Assert.DoesNotContain("each of them", s);
    }
}
