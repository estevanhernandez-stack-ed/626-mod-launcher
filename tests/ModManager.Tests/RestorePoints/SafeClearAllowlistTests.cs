using System.Diagnostics;
using System.Security.Cryptography;
using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 4 review (C1 and I1 to I5, M1 to M4): vanilla sweeps ONLY folders the launcher knows hold nothing
/// but mods (the declared extra trees, the per-engine shapes in <see cref="ModOnlyFolders"/>, and a modPath
/// the game's definition marks <c>modPathModOnly</c>), and turns off only rows in such folders or on a lane
/// with its own mechanism. Every probe shape from the review that planned to move base-game content is a
/// regression here: zero planned files, nothing in the game folder touched, and no vanilla claim.
/// </summary>
public class SafeClearAllowlistTests : IDisposable
{
    private const string Ts = "20261002-160000";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-allow-" + Guid.NewGuid().ToString("n"));
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

    private RestorePointOrchestrator Make(GameEntry g) => Make(new Provider(new[] { g }));
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

    // ---- The table: one test per shape, and the neighbours that must NOT match ----

    [Theory]
    [InlineData("ue-pak", "Game/Content/Paks/~mods")]
    [InlineData("ue-pak", "Content/Paks/~mods")]
    [InlineData("ue-pak", "Pal/Content/Paks/LogicMods")]
    [InlineData("ue-pak", "Game/Content/Paks/Mods")]
    [InlineData("bepinex", "BepInEx/plugins")]
    [InlineData("smapi", "Mods")]
    [InlineData("melonloader", "Mods")]
    [InlineData("minecraft", "mods")]
    [InlineData("fromsoft", "mod")]
    [InlineData("fromsoft", "ModEngine2/mod")]
    public void Each_engine_shape_is_mod_only(string engine, string rel)
        => Assert.True(ModOnlyFolders.IsShape(engine, rel));

    [Theory]
    [InlineData("ue-pak", "Game/Content/Paks")]                       // files-form Content/Paks: base paks live here
    [InlineData("ue-pak", "TBL/Content/Paks")]
    [InlineData("ue-pak", "ShooterGame/Binaries/Win64/ShooterGame/Mods")]   // ARK:SA, in-game CurseForge's folder
    [InlineData("bethesda", "Data")]
    [InlineData("bethesda", "Data Files")]
    [InlineData("fromsoft", "DATA")]                                  // DS PTDE: the exe and archives
    [InlineData("fromsoft", "Game")]
    [InlineData("source", "addons")]
    [InlineData("source", "addons/workshop")]
    [InlineData("source", "Vampire")]
    [InlineData("custom", "data")]
    [InlineData("custom", "Modules")]
    [InlineData("custom", "Win64/ovldata")]
    [InlineData("custom", "archive/pc/mod")]                          // only via the definition's flag
    [InlineData("minecraft", "")]
    [InlineData(null, "mods")]
    public void Base_content_and_unknown_shapes_are_not(string? engine, string rel)
        => Assert.False(ModOnlyFolders.IsShape(engine, rel));

    // ---- C1 + I5: every probe shape plans nothing, touches nothing, and claims nothing ----

    public static IEnumerable<object[]> BaseContentGames()
    {
        // (id, engine, modPath, files relative to the game root, form)
        yield return new object[] { "skyrim", "bethesda", "Data", new[]
        {
            "Data/Skyrim.esm", "Data/Update.esm", "Data/ccBGSSSE001-Fish.esm", "Data/Skyrim - Textures0.bsa",
            "Data/Strings/Skyrim_English.strings", "Data/Video/BGS_Logo.bik", "Data/SomeMod.esp",
        }, "" };
        yield return new object[] { "totalwar", "custom", "data", new[] { "data/data.pack", "data/local_en.pack", "data/manifest.txt" }, "" };
        yield return new object[] { "bannerlord", "custom", "Modules", new[] { "Modules/Native/SubModule.xml", "Modules/SandBox/SubModule.xml" }, "" };
        yield return new object[] { "dsptde", "fromsoft", "DATA", new[] { "DATA/DARKSOULS.exe", "DATA/dvdbnd0.bdt" }, "folders" };
        yield return new object[] { "bloodlines", "source", "Vampire", new[] { "Vampire/pack000.vpk", "Vampire/python/x.py" }, "" };
        yield return new object[] { "jwe2", "custom", "Win64/ovldata", new[] { "Win64/ovldata/Main.ovl", "Win64/ovldata/Content0/x.ovl" }, "" };
        yield return new object[] { "chivalry2", "ue-pak", "TBL/Content/Paks", new[]
        {
            "TBL/Content/Paks/pakchunk0-WindowsNoEditor.pak", "TBL/Content/Paks/pakchunk0-WindowsNoEditor.sig",
            "TBL/Content/Paks/pakchunk0-Windows.utoc", "TBL/Content/Paks/global.ucas", "TBL/Content/Paks/global.utoc",
        }, "files" };
        yield return new object[] { "customuser", "custom", "Content", new[] { "Content/base.dat" }, "" };
    }

    [Theory]
    [MemberData(nameof(BaseContentGames))]
    public async Task A_base_content_mod_path_plans_nothing_moves_nothing_and_never_claims_vanilla(
        string id, string engine, string modPath, string[] files, string form)
    {
        var root = Path.Combine(_root, id);
        Put(Path.Combine(root, "game.exe"), "EXE");
        foreach (var f in files) Put(Path.Combine(root, Path.Combine(f.Split('/'))), "BASE " + f);
        var g = new GameEntry
        {
            Id = id, GameName = id, Engine = engine, GameRoot = root, DataDir = DataDir(id),
            FileExtensions = files.Select(f => Path.GetExtension(f).TrimStart('.')).Where(e => e.Length > 0).Distinct().ToArray(),
            UserSet = new List<string> { GameEntry.UserSetModLocations },
            ModLocations = new[] { new ModLocation("mods", "Mods", modPath) { Form = form } },
        };
        var before = Snapshot(root);

        // The plan alone, the way the review's probe called it: nothing to move.
        Assert.Empty(RestorePointEngine.PlanVanillaRemainder(Scanner.GameContext(g), Array.Empty<ClearSkip>()).Files);

        var r = await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default);

        Assert.True(r.Ok, r.RefusedReason);
        AssertSameTree(before, root);   // no base file moved: not by the sweep, not by a turn-off
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Empty(ga.VanillaRemainder!);
        Assert.DoesNotContain(ga.TurnedOffByClear!, m => m.Location == "mods");
        Assert.Contains(ga.LeftInPlace!, n => n.Reason.Contains("can't tell the game's own files from mods"));
        Assert.False(OffBoardingHydrator.FullyVanilla(ga));
        Assert.DoesNotContain("returned to vanilla", OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, RpDir)));
    }

    [Fact]
    public async Task Skyrims_base_masters_are_rows_left_on_and_listed_as_still_active()
    {
        var root = Path.Combine(_root, "skyrim");
        Put(Path.Combine(root, "Data", "Skyrim.esm"), "MASTER");
        Put(Path.Combine(root, "Data", "SomeMod.esp"), "MOD");
        var g = new GameEntry
        {
            Id = "skyrim", GameName = "Skyrim", Engine = "bethesda", GameRoot = root, DataDir = DataDir("skyrim"),
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "Data") },
        };
        // Evidence for the controller (Disable All has the same base-master risk): the scanner lists the base
        // master as an ordinary, switchable row. Nothing in the files form filters it out.
        Assert.Contains(ModListing.Resolve(g), m => m.Name == "Skyrim" && m.Enabled && !m.ReadOnly);

        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.Equal("MASTER", File.ReadAllText(Path.Combine(root, "Data", "Skyrim.esm")));
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Empty(ga.TurnedOffByClear!);
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "Skyrim" && n.Reason.StartsWith("still active"));
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "SomeMod" && n.Reason.StartsWith("still active"));
    }

    [Fact]
    public void Cyberpunks_mod_folder_is_mod_only_because_its_definition_says_so()
    {
        var root = Path.Combine(_root, "cp");
        Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "mod"));
        var g = new GameEntry
        {
            Id = "cyberpunk-2077", GameName = "Cyberpunk 2077", Engine = "custom", SteamAppId = "1091500", GameRoot = root,
            DataDir = DataDir("cp"), FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
        var c = Scanner.GameContext(g);
        Assert.Equal("marked mod-only by the game's definition", ModOnlyFolders.WhyModOnly(c, c.Locations[0]));

        // A user who set the locations by hand gets no such promise from the definition.
        g.UserSet = new List<string> { GameEntry.UserSetModLocations };
        var c2 = Scanner.GameContext(g);
        Assert.Null(ModOnlyFolders.WhyModOnly(c2, c2.Locations[0]));
    }

    // ---- I1: links are never followed ----

    private static bool TryJunction(string link, string target)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            return Directory.Exists(link);
        }
        catch { return false; }
    }

    [WindowsFact]
    public void A_junction_inside_a_mod_folder_is_named_and_never_followed()
    {
        var root = Path.Combine(_root, "bep");
        var outside = Path.Combine(_root, "shared-library");
        Put(Path.Combine(root, "BepInEx", "plugins", "real.txt"), "REAL");
        Put(Path.Combine(outside, "Shared.dll"), "SHARED");
        Assert.True(TryJunction(Path.Combine(root, "BepInEx", "plugins", "linked"), outside), "mklink /J failed");
        var g = new GameEntry
        {
            Id = "bep", GameName = "Bep", Engine = "bepinex", GameRoot = root, DataDir = DataDir("bep"),
            FileExtensions = new[] { "dll" }, ModLocations = new[] { new ModLocation("mods", "Mods", "BepInEx/plugins") },
        };

        var plan = RestorePointEngine.PlanVanillaRemainder(Scanner.GameContext(g), Array.Empty<ClearSkip>());

        Assert.Contains(plan.Files, f => f.Rel.Replace('\\', '/') == "BepInEx/plugins/real.txt");
        Assert.DoesNotContain(plan.Files, f => f.Rel.Contains("Shared.dll"));
        Assert.Contains(plan.LeftInPlace, n => n.Path.Replace('\\', '/') == "BepInEx/plugins/linked" && n.Reason.Contains("link"));
        Directory.Delete(Path.Combine(root, "BepInEx", "plugins", "linked"));
    }

    [WindowsFact]
    public void A_mod_folder_that_is_itself_a_junction_is_not_swept()
    {
        var root = Path.Combine(_root, "bep2");
        var outside = Path.Combine(_root, "elsewhere");
        Put(Path.Combine(outside, "Shared.dll"), "SHARED");
        Directory.CreateDirectory(Path.Combine(root, "BepInEx"));
        Assert.True(TryJunction(Path.Combine(root, "BepInEx", "plugins"), outside), "mklink /J failed");
        var g = new GameEntry
        {
            Id = "bep2", GameName = "Bep2", Engine = "bepinex", GameRoot = root, DataDir = DataDir("bep2"),
            FileExtensions = new[] { "dll" }, ModLocations = new[] { new ModLocation("mods", "Mods", "BepInEx/plugins") },
        };

        var plan = RestorePointEngine.PlanVanillaRemainder(Scanner.GameContext(g), Array.Empty<ClearSkip>());

        Assert.Empty(plan.Files);
        Assert.Contains(plan.LeftInPlace, n => n.Reason.Contains("link"));
        Directory.Delete(Path.Combine(root, "BepInEx", "plugins"));
    }

    // ---- I2: a file that reached the archive keeps its record ----

    [Fact]
    public void A_check_that_fails_after_the_move_keeps_the_archived_file_on_record()
    {
        var root = Path.Combine(_root, "mc");
        Put(Path.Combine(root, "mods", "a.txt"), "A");
        Put(Path.Combine(root, "mods", "b.txt"), "B");
        var g = new GameEntry
        {
            Id = "mc", GameName = "MC", Engine = "minecraft", GameRoot = root, DataDir = DataDir("mc"),
            FileExtensions = new[] { "jar" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var c = Scanner.GameContext(g);
        var plan = RestorePointEngine.PlanVanillaRemainder(c, Array.Empty<ClearSkip>());
        Assert.Equal(2, plan.Files.Count);
        var dir = Path.Combine(_root, "arch");

        RestorePointEngine.AfterRemainderMoveForTests = dest =>
        {
            if (dest.EndsWith("a.txt", StringComparison.OrdinalIgnoreCase)) throw new IOException("injected: the checksum read failed");
        };
        IReadOnlyList<InPlaceNote> stayed;
        try { stayed = RestorePointEngine.SweepRemainder(c, plan.Files, dir); }
        finally { RestorePointEngine.AfterRemainderMoveForTests = null; }

        Assert.Empty(stayed);   // a.txt is in the archive, not live: it keeps its record
        Assert.False(File.Exists(Path.Combine(root, "mods", "a.txt")));
        Assert.Equal("A", File.ReadAllText(Path.Combine(dir, RestorePointEngine.RemainderDirName, "mods", "a.txt")));
    }

    // ---- I3: ban risk keeps the remainder in the restore point ----

    private GameEntry BanRiskMinecraft()
    {
        var root = Path.Combine(_root, "ban");
        Put(Path.Combine(root, "mods", "alpha.pak"), "ALPHA");
        Put(Path.Combine(root, "mods", "config", "loose.cfg"), "LOOSE");
        return new GameEntry
        {
            Id = "ban", GameName = "Ban Game", Engine = "minecraft", SteamAppId = "4032350", GameRoot = root, DataDir = DataDir("ban"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
    }

    [Fact]
    public async Task On_an_unacknowledged_ban_risk_game_the_remainder_stays_in_the_restore_point()
    {
        var g = BanRiskMinecraft();
        var orch = Make(g);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Assert.False(File.Exists(Path.Combine(g.GameRoot, "mods", "config", "loose.cfg")));

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.False(File.Exists(Path.Combine(g.GameRoot, "mods", "config", "loose.cfg")));
        Assert.Contains(restore.Warnings, w => w.Contains(RestorePointEngine.RemainderKeptForBanRisk));
        Assert.True(File.Exists(Path.Combine(RpDir, "games", "ban", RestorePointEngine.RemainderDirName, "mods", "config", "loose.cfg")));
    }

    [Fact]
    public async Task On_an_acknowledged_ban_risk_game_the_remainder_comes_back()
    {
        var g = BanRiskMinecraft();
        BanRiskAckStore.Ack(g.DataDir!, g.Id);
        var before = Snapshot(g.GameRoot);
        var orch = Make(g);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, g.GameRoot);
    }

    // ---- I4: every folder not swept is named, so vanilla is never claimed over it ----

    [Fact]
    public async Task A_loose_root_game_names_the_game_folder_and_never_claims_vanilla()
    {
        var root = Path.Combine(_root, "decima");
        Put(Path.Combine(root, "game.exe"), "EXE");
        var g = new GameEntry
        {
            Id = "decima", GameName = "Decima", Engine = "decima", GameRoot = root, DataDir = DataDir("decima"),
            ModLocations = new[] { new ModLocation("mods", "Game folder", ".") },
        };
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "the game folder itself");
        Assert.False(OffBoardingHydrator.FullyVanilla(ga));
    }

    [Fact]
    public async Task A_direct_inject_game_names_its_play_folder_and_never_claims_vanilla()
    {
        var root = Path.Combine(_root, "er");
        Put(Path.Combine(root, "Game", "eldenring.exe"), "EXE");
        var g = new GameEntry { Id = "er", GameName = "ER", Engine = "fromsoft", GameRoot = root, DataDir = DataDir("er") };
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "Game");
        Assert.False(OffBoardingHydrator.FullyVanilla(ga));
    }

    [Fact]
    public async Task A_mod_folder_outside_the_game_is_never_swept_but_its_mods_turn_off_and_come_back()
    {
        // Round 7 (review r6, I-1): outside the game folder a row is turned off ONLY with proof that 626
        // installed it. cc.package has an install record: off, and back on restore. other.package has none:
        // it stays on, named. The folder itself is never swept: the loose file there stays, named.
        var root = Path.Combine(_root, "sims");
        var docs = Path.Combine(_root, "Documents", "Electronic Arts", "The Sims 4", "Mods");
        Put(Path.Combine(root, "game.exe"), "EXE");
        Put(Path.Combine(docs, "cc.package"), "CC");
        Put(Path.Combine(docs, "other.package"), "OTHER");
        Put(Path.Combine(docs, "Resource.cfg"), "CFG");
        var g = new GameEntry
        {
            Id = "sims", GameName = "Sims", Engine = "custom", GameRoot = root, DataDir = DataDir("sims"),
            FileExtensions = new[] { "package" }, ModLocations = new[] { new ModLocation("mods", "Mods", docs) },
        };
        ModInstallRegistry.Save(g.DataDir!, new ModInstallManifest("cc", "cc.zip", "mods", new[] { "cc.package" }, DateTime.UtcNow.AddMinutes(1)));
        var before = Snapshot(docs);
        var orch = Make(g);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.False(File.Exists(Path.Combine(docs, "cc.package")));       // 626 installed it: held
        Assert.Equal("OTHER", File.ReadAllText(Path.Combine(docs, "other.package")));   // no proof: left on
        Assert.Equal("CFG", File.ReadAllText(Path.Combine(docs, "Resource.cfg")));   // the folder is not swept
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Contains(ga.TurnedOffByClear!, m => m.Name == "cc");
        Assert.Empty(ga.VanillaRemainder!);
        Assert.Contains(ga.LeftInPlace!, n => n.Reason.Contains("can't tell the game's own files from mods"));
        Assert.False(OffBoardingHydrator.FullyVanilla(ga));

        var restore = await orch.RestoreAsync(Ts, default);
        Assert.True(restore.Ok);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, docs);
    }

    // ---- M1: the pre-flight adds only holds that come from outside the mod-only folders ----

    [Fact]
    public void M1_the_turn_off_estimate_for_the_pre_flight_counts_only_holds_outside_mod_only_folders()
    {
        var root = Path.Combine(_root, "mc1");
        Put(Path.Combine(root, "mods", "alpha.pak"), "ALPHA");
        var g = new GameEntry
        {
            Id = "mc1", GameName = "MC1", Engine = "minecraft", GameRoot = root, DataDir = DataDir("mc1"),
            FileExtensions = new[] { "pak" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var c = Scanner.GameContext(g);

        Assert.Equal(5, RestorePointEngine.EstimateTurnOffBytes(c));
        Assert.Equal(0, RestorePointEngine.EstimateTurnOffBytes(c, outsideModOnlyOnly: true));
        Assert.Equal(5, RestorePointEngine.EstimateModOnlyBytes(c));
    }

    // ---- M2: the framework exclusion reads the sealed capture, not the emptied registry ----

    [Fact]
    public void M2_a_captured_frameworks_files_are_never_swept_even_with_the_registry_empty()
    {
        var root = Path.Combine(_root, "mc2");
        Put(Path.Combine(root, "mods", "fwlib.jar"), "FRAMEWORK");
        Put(Path.Combine(root, "mods", "loose.txt"), "LOOSE");
        var g = new GameEntry
        {
            Id = "mc2", GameName = "MC2", Engine = "minecraft", GameRoot = root, DataDir = DataDir("mc2"),
            FileExtensions = new[] { "pak" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var captured = new[] { new FrameworkArchive("fw", "FW", "a", Path.Combine(root, "mods"), new[] { "fwlib.jar" }, null) };

        var plan = RestorePointEngine.PlanVanillaRemainder(Scanner.GameContext(g), Array.Empty<ClearSkip>(), captured);

        Assert.DoesNotContain(plan.Files, f => f.Rel.EndsWith("fwlib.jar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.Files, f => f.Rel.EndsWith("loose.txt", StringComparison.OrdinalIgnoreCase));
    }

    // ---- M3: a Mod Engine 2 mod's folder is its config's; a mod missing remainder files stays off ----

    [Fact]
    public async Task M3_a_Mod_Engine_2_mods_folder_is_left_to_its_config_and_the_rest_is_swept()
    {
        var root = Path.Combine(_root, "me2");
        var me2 = Path.Combine(root, "ModEngine2");
        Put(Path.Combine(root, "Game", "eldenring.exe"), "EXE");
        Put(Path.Combine(me2, "mod", "CoolMod", "parts", "x.dcx"), "COOL");
        Put(Path.Combine(me2, "mod", "stray.txt"), "STRAY");
        var config = Path.Combine(me2, "config_eldenring.toml");
        File.WriteAllText(config, "[extension.mod_loader]\nenabled = true\nmods = [\n    { enabled = true, name = \"CoolMod\", path = \"mod/CoolMod\" }\n]\n");
        var g = new GameEntry
        {
            Id = "me2", GameName = "ME2 Game", Engine = "fromsoft", GameRoot = root, DataDir = DataDir("me2"),
            ModEngineConfig = config, ModLocations = new[] { new ModLocation("mods", "Mods", "ModEngine2/mod") { Form = "folders" } },
        };
        var configBefore = File.ReadAllText(config);
        var before = Snapshot(root);
        var orch = Make(g);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Contains(ga.VanillaRemainder!, f => f.Rel.Replace('\\', '/') == "ModEngine2/mod/stray.txt");
        Assert.DoesNotContain(ga.VanillaRemainder!, f => f.Rel.Contains("CoolMod"));
        Assert.Equal("COOL", File.ReadAllText(Path.Combine(me2, "mod", "CoolMod", "parts", "x.dcx")));   // config-off, not moved

        Assert.True((await orch.RestoreAsync(Ts, default)).Ok);
        Assert.Equal(configBefore, File.ReadAllText(config));
        foreach (var (rel, sha) in before.Where(kv => !kv.Key.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)))
            Assert.Equal(sha, Snapshot(root)[rel]);
    }

    [Fact]
    public async Task M3_a_mod_whose_remainder_files_did_not_come_back_is_left_off()
    {
        var root = Path.Combine(_root, "mc3");
        Put(Path.Combine(root, "mods", "Cool", "main.bin"), "COOL");
        var g = new GameEntry
        {
            Id = "mc3", GameName = "MC3", Engine = "minecraft", GameRoot = root, DataDir = DataDir("mc3"),
            FileExtensions = new[] { "bin" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") { Form = "folders" } },
        };
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Contains(ga.TurnedOffByClear!, m => m.Name == "Cool");
        // A remainder record inside Cool's folder whose archived copy is gone.
        ga = ga with { VanillaRemainder = new[] { new MovedFile(Path.Combine("mods", "Cool", "extra.txt"), 1, "00") } };

        var r = RestorePointEngine.ReplayGame(ga, Path.Combine(RpDir, "games", g.Id), p.ContextFor(g));

        Assert.Contains(r.RemainderIssues, i => i.Name.EndsWith("extra.txt"));
        Assert.Contains(r.NotBackOn, s => s.Name == "Cool" && s.Reason.Contains("didn't come back"));
        Assert.False(Directory.Exists(Path.Combine(root, "mods", "Cool")));
    }

    // ---- M4: a remainder path must be under a folder the launcher knows holds only mods ----

    [Fact]
    public async Task M4_a_remainder_record_outside_the_mod_only_folders_is_refused()
    {
        var root = Path.Combine(_root, "mc4");
        Put(Path.Combine(root, "mods", "loose.txt"), "LOOSE");
        var g = new GameEntry
        {
            Id = "mc4", GameName = "MC4", Engine = "minecraft", GameRoot = root, DataDir = DataDir("mc4"),
            FileExtensions = new[] { "jar" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var p = new Provider(new[] { g });
        Assert.True((await Make(p).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var dir = Path.Combine(RpDir, "games", g.Id);
        Put(Path.Combine(dir, RestorePointEngine.RemainderDirName, "dinput8.dll"), "PLANTED");
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        ga = ga with { VanillaRemainder = ga.VanillaRemainder!.Append(new MovedFile("dinput8.dll", 7, FileTally.Sha256(Path.Combine(dir, RestorePointEngine.RemainderDirName, "dinput8.dll")))).ToList() };

        var r = RestorePointEngine.ReplayGame(ga, dir, p.ContextFor(g));

        Assert.Contains(r.RemainderIssues, i => i.Name == "dinput8.dll" && i.Reason.Contains("holds only mods"));
        Assert.False(File.Exists(Path.Combine(root, "dinput8.dll")));
        Assert.Equal("LOOSE", File.ReadAllText(Path.Combine(root, "mods", "loose.txt")));   // the real record came back
    }
}
