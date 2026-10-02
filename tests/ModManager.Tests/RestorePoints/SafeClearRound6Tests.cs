using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 6 (review r5):
/// <list type="bullet">
/// <item>I-A: the live feed restating Cyberpunk's modPath without the flag keeps the snapshot's flag.</item>
/// <item>I-B: a row that replaced a game file is never "placed by 626".</item>
/// <item>I-C: system folders (an ancestor of the game, a drive, the profile and its well-known children,
/// Windows, Program Files, ProgramData) get no turn-offs and no sweep.</item>
/// <item>The minors: ownership hardening, folder-form ownership, sheet counts, executable folders in the
/// flag's base list, and UE4's project-named base pak.</item>
/// </list>
/// </summary>
public class SafeClearRound6Tests : IDisposable
{
    private const string Ts = "20261002-200000";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-r6-" + Guid.NewGuid().ToString("n"));
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

    // ---- I-A: the merge, unit level ----

    private static bool? Merged(GameManifestEntry embedded, GameManifestEntry remote)
        => EffectiveManifest.Merge(new GameManifest { Games = new[] { embedded } }, new GameManifest { Games = new[] { remote } })
            .Games.Single().ModPathModOnly;

    private static GameManifestEntry Cp(string? modPath, bool? flag) => new() { Id = "cyberpunk-2077", ModPath = modPath, ModPathModOnly = flag };

    [Theory]
    [InlineData("archive/pc/mod")]
    [InlineData("Archive/PC/Mod")]
    [InlineData("archive\\pc\\mod\\")]
    [InlineData("/archive/pc/mod/")]
    public void A_remote_restating_the_same_path_without_the_flag_keeps_the_snapshots_true(string restated)
        => Assert.True(Merged(Cp("archive/pc/mod", true), Cp(restated, null)));

    [Fact]
    public void A_remote_restating_the_same_path_with_an_explicit_false_overrides()
        => Assert.False(Merged(Cp("archive/pc/mod", true), Cp("archive/pc/mod", false)));

    [Fact]
    public void A_remote_changing_the_path_still_drops_an_inherited_true()
        => Assert.Null(Merged(Cp("archive/pc/mod", true), Cp("archive/pc/content", null)));

    [Fact]
    public void Absent_in_JSON_reads_as_null_and_false_in_JSON_reads_as_false()
    {
        var absent = JsonSerializer.Deserialize<GameManifest>("{\"games\":[{\"id\":\"x\",\"modPath\":\"m\"}]}", ManifestJson.Options)!;
        var stated = JsonSerializer.Deserialize<GameManifest>("{\"games\":[{\"id\":\"x\",\"modPath\":\"m\",\"modPathModOnly\":false}]}", ManifestJson.Options)!;
        Assert.Null(absent.Games.Single().ModPathModOnly);
        Assert.False(stated.Games.Single().ModPathModOnly);
    }

    // ---- I-B: a row that replaced a game file stays on ----

    [Fact]
    public async Task A_real_intake_that_replaced_Skyrims_Update_esm_leaves_it_on_and_named_and_the_new_plugin_goes()
    {
        var root = Path.Combine(_root, "skyrim");
        var data = Path.Combine(root, "Data");
        Put(Path.Combine(data, "Skyrim.esm"), "MASTER");
        Put(Path.Combine(data, "Update.esm"), "ORIGINAL UPDATE");
        var g = new GameEntry
        {
            Id = "skyrim", GameName = "Skyrim", Engine = "bethesda", GameRoot = root, DataDir = DataDir("skyrim"),
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "Data") },
        };
        var c = Scanner.GameContext(g);
        var drop = Path.Combine(_root, "drop");
        Put(Path.Combine(drop, "Update.esm"), "CLEANED UPDATE");
        Put(Path.Combine(drop, "NewMod.esp"), "NEW MOD");
        var plan = Scanner.PlanIntake(new[] { Path.Combine(drop, "Update.esm"), Path.Combine(drop, "NewMod.esp") }, c);
        Assert.Contains(plan.Collisions, x => x.Name == "Update.esm");
        Scanner.ExecuteIntake(plan, new HashSet<string> { "Update.esm" }, c);
        Assert.Equal("CLEANED UPDATE", File.ReadAllText(Path.Combine(data, "Update.esm")));
        var before = Snapshot(root);
        var orch = Make(g);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.Equal("CLEANED UPDATE", File.ReadAllText(Path.Combine(data, "Update.esm")));   // the game is never left without it
        Assert.Equal("MASTER", File.ReadAllText(Path.Combine(data, "Skyrim.esm")));
        Assert.False(File.Exists(Path.Combine(data, "NewMod.esp")));                          // 626 placed it: off
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.DoesNotContain(ga.TurnedOffByClear!, m => m.Name == "Update");
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "Update" && n.Reason == RestorePointEngine.ReplacedGameFileNote);

        var restore = await orch.RestoreAsync(Ts, default);
        Assert.True(restore.Ok);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, root);
    }

    // ---- I-C: system folders ----

    [Fact]
    public async Task A_location_that_is_an_ancestor_of_the_game_gets_no_turn_offs_and_no_sweep()
    {
        var common = Path.Combine(_root, "steamapps", "common");
        var root = Path.Combine(common, "TheGame");
        Put(Path.Combine(root, "game.exe"), "EXE");
        Put(Path.Combine(root, "Data", "base.pak"), "BASE");
        Put(Path.Combine(common, "OtherGame", "other.exe"), "OTHER");
        var g = new GameEntry
        {
            Id = "anc", GameName = "Anc", Engine = "custom", GameRoot = root, DataDir = DataDir("anc"),
            FileExtensions = new[] { "pak" },
            ModLocations = new[] { new ModLocation("mods", "Mods", common) { Form = "folders" } },
        };
        var before = Snapshot(common);

        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        AssertSameTree(before, common);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Empty(ga.TurnedOffByClear!);
        Assert.Empty(ga.VanillaRemainder!);
        Assert.Contains(ga.LeftInPlace!, n => n.Reason.StartsWith(RestorePointEngine.SystemFolderPrefix) && n.Reason.Contains("contains the game folder"));
    }

    public static IEnumerable<object[]> SystemFolders()
    {
        foreach (var sf in new[]
                 {
                     Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData,
                     Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.MyDocuments,
                     Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Windows,
                     Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.CommonApplicationData,
                 })
        {
            var p = Environment.GetFolderPath(sf);
            if (!string.IsNullOrEmpty(p)) yield return new object[] { sf.ToString(), p };
        }
        var drive = Path.GetPathRoot(Path.GetTempPath());
        if (!string.IsNullOrEmpty(drive)) yield return new object[] { "drive root", drive };
    }

    [Theory]
    [MemberData(nameof(SystemFolders))]
    public void A_system_folder_location_plans_no_turn_off_and_no_sweep_and_is_named(string what, string folder)
    {
        // Plan only: nothing here is ever run against a real system folder. "dat"/"sys"/"ini" are what the
        // review's probe showed would have planned NTUSER.DAT, pagefile.sys and friends.
        var root = Path.Combine(_root, "game-" + what.Replace(' ', '-'));
        Put(Path.Combine(root, "game.exe"), "EXE");
        var g = new GameEntry
        {
            Id = "sys", GameName = "Sys", Engine = "custom", GameRoot = root, DataDir = DataDir("sys-" + what.Replace(' ', '-')),
            FileExtensions = new[] { "dat", "sys", "ini" },
            ModLocations = new[] { new ModLocation("mods", "Mods", folder) },
        };
        var c = Scanner.GameContext(g);

        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(c, Array.Empty<MovedFile>()));
        var plan = RestorePointEngine.PlanVanillaRemainder(c, Array.Empty<ClearSkip>());
        Assert.Empty(plan.Files);
        Assert.Contains(plan.LeftInPlace, n => n.Reason.StartsWith(RestorePointEngine.SystemFolderPrefix));
        Assert.NotNull(RestorePointEngine.SystemFolderReason(c, folder));
    }

    [Fact]
    public void A_game_folder_under_Documents_is_not_a_system_folder()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Assert.False(string.IsNullOrEmpty(docs));
        var c = Scanner.GameContext(new GameEntry { Id = "x", GameName = "X", GameRoot = Path.Combine(_root, "g"), DataDir = DataDir("x") });
        Assert.Null(RestorePointEngine.SystemFolderReason(c, Path.Combine(docs, "Electronic Arts", "The Sims 4", "Mods")));
    }

    // ---- m-a: ownership hardening ----

    private GameEntry SkyrimWith(params (string File, string Content)[] files)
    {
        var root = Path.Combine(_root, "sk" + Guid.NewGuid().ToString("n")[..6]);
        foreach (var (f, v) in files) Put(Path.Combine(root, "Data", f), v);
        return new GameEntry
        {
            Id = "sk", GameName = "Skyrim", Engine = "bethesda", GameRoot = root, DataDir = DataDir("sk" + Path.GetFileName(root)),
            FileExtensions = new[] { "esp", "esm", "bsa" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "Data") },
        };
    }

    private static void Record(GameEntry g, DateTime installedUtc, params string[] files)
        => ModInstallRegistry.Save(g.DataDir!, new ModInstallManifest("rec" + files.Length, "rec.zip", "mods", files, installedUtc));

    [Fact]
    public void m_a_a_record_whose_file_changed_after_the_install_does_not_make_it_626s()
    {
        var g = SkyrimWith(("SomeMod.esp", "MOD"));
        Record(g, DateTime.UtcNow.AddDays(-2), "SomeMod.esp");   // installed two days ago; the file is newer
        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
    }

    [Theory]
    [InlineData("..\\Data\\SomeMod.esp")]
    [InlineData("\\SomeMod.esp")]
    public void m_a_a_rooted_or_climbing_record_entry_never_matches(string entry)
    {
        var g = SkyrimWith(("SomeMod.esp", "MOD"));
        Record(g, DateTime.UtcNow.AddMinutes(1), entry);
        Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
    }

    [Fact]
    public void m_a_a_fresh_record_of_the_files_own_name_does_match()
    {
        var g = SkyrimWith(("SomeMod.esp", "MOD"));
        Record(g, DateTime.UtcNow.AddMinutes(1), "SomeMod.esp");
        Assert.Single(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(g), Array.Empty<MovedFile>()));
    }

    // ---- m-b: folder-form mods qualify when every file under the folder is recorded ----

    private GameEntry KspWith(bool recordAll)
    {
        var root = Path.Combine(_root, "ksp" + (recordAll ? "a" : "p"));
        Put(Path.Combine(root, "GameData", "Squad", "base.cfg"), "BASE");
        Put(Path.Combine(root, "GameData", "MyMod", "part.cfg"), "PART");
        Put(Path.Combine(root, "GameData", "MyMod", "Plugins", "my.dll"), "DLL");
        var g = new GameEntry
        {
            Id = "ksp", GameName = "KSP", Engine = "custom", GameRoot = root, DataDir = DataDir("ksp" + (recordAll ? "a" : "p")),
            GroupingRule = "by_folder",
            ModLocations = new[] { new ModLocation("mods", "GameData", "GameData") { Form = "folders" } },
        };
        Record(g, DateTime.UtcNow.AddMinutes(1), recordAll
            ? new[] { "MyMod/part.cfg", "MyMod/Plugins/my.dll" }
            : new[] { "MyMod/part.cfg" });
        return g;
    }

    [Fact]
    public void m_b_a_626_installed_folder_mod_is_turned_off_and_the_base_folder_is_not()
    {
        var set = RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(KspWith(recordAll: true)), Array.Empty<MovedFile>());
        Assert.Equal(new[] { "MyMod" }, set.Select(m => m.Name));
    }

    [Fact]
    public void m_b_a_folder_mod_with_an_unrecorded_file_stays_on()
        => Assert.Empty(RestorePointEngine.PlanVanillaTurnOffs(Scanner.GameContext(KspWith(recordAll: false)), Array.Empty<MovedFile>()));

    // ---- m-c: sheet counts ----

    [Fact]
    public async Task m_c_a_vanilla_Skyrim_never_reads_as_mods_still_active()
    {
        var g = SkyrimWith(("Skyrim.esm", "M"), ("Update.esm", "U"), ("Dawnguard.esm", "D"), ("HearthFires.esm", "H"),
            ("Dragonborn.esm", "DB"), ("Skyrim - Textures0.bsa", "T"), ("Skyrim - Meshes0.bsa", "ME"));
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        var line = OffBoardingHydrator.Hydrate(ga, RpDir).LaunchLines.Single();
        Assert.DoesNotContain("mods are still active", line);
        Assert.DoesNotContain("mod is still active", line);
        Assert.Contains("626 didn't turn off any mods here", line);
        Assert.Contains("7 items 626 can't tell from the game's own files are still in place", line);
    }

    [Fact]
    public void m_c_a_base_pak_row_is_counted_once()
    {
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla", Array.Empty<LaunchTarget>(), null,
            Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null,
            TurnedOffByClear: new[] { new ClearedMod("CoolMod_P", "mods") },
            VanillaRemainder: Array.Empty<MovedFile>(),
            LeftInPlace: new[]
            {
                new InPlaceNote("pakchunk0-Windows", RestorePointEngine.BasePakRowNote),
                new InPlaceNote("pakchunk0-Windows", RestorePointEngine.CantTellRowPrefix + "Content/Paks, where 626 can't tell"),
                new InPlaceNote("Content/Paks", "626 can't tell the game's own files from mods in Content/Paks; 1 files no mod claims are still in place"),
            });

        var line = OffBoardingHydrator.WhatHappened(ga);

        Assert.StartsWith("626 turned off the 1 mod it could tell were mods", line);   // never "all" beside items
        Assert.Contains("1 item 626 can't tell from the game's own files is still in place", line);
    }

    // ---- m-d: executable folders and trailing dots/spaces ----

    [Theory]
    [InlineData("bin")]
    [InlineData("bin/x64")]
    [InlineData("Binaries")]
    [InlineData("Game/Binaries/Win64")]
    [InlineData("x64")]
    [InlineData("Data.")]
    [InlineData("Data ")]
    [InlineData(" Data")]
    [InlineData("x/Data./")]
    public void m_d_executable_folders_and_disguised_base_names_never_carry_the_flag(string modPath)
        => Assert.NotNull(ModOnlyFolders.ModOnlyFlagProblem(modPath));

    // ---- m-e was reverted in round 7: a project-named pak is a MOD unless proven base ----
    // (SafeClearRound7Tests.A_project_named_mod_pak_stays_a_visible_toggleable_mod pins it.)
}

/// <summary>I-A against the published feed's shape: Cyberpunk restated with <c>archive/pc/mod</c> and no flag,
/// read with the real JSON options, validated, and applied as the live remote.</summary>
[Collection("ManifestState")]
public class SafeClearPublishedFeedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-feed-" + Guid.NewGuid().ToString("n"));
    public void Dispose()
    {
        EffectiveManifest.SetRemote(null);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // The published entry, field for field as the feed's miner writes it: no modPathModOnly.
    private const string PublishedFeed = """
    {
      "schemaVersion": 1,
      "games": [
        {
          "id": "cyberpunk-2077",
          "name": "Cyberpunk 2077",
          "engine": "custom",
          "stores": { "steamAppId": "1091500" },
          "nexusDomain": "cyberpunk2077",
          "modPath": "archive/pc/mod",
          "fileExtensions": [ "archive" ],
          "featured": 10,
          "extraModTrees": [ "r6/scripts", "r6/tweaks", "r6/input", "red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods" ],
          "provenance": { "sources": [ "nexus-domains", "popular-games" ], "status": "curated" }
        }
      ]
    }
    """;

    [Fact]
    public void Cyberpunk_restated_by_the_live_feed_without_the_flag_still_sweeps()
    {
        var remote = JsonSerializer.Deserialize<GameManifest>(PublishedFeed, ManifestJson.Options)!;
        Assert.Null(remote.Games.Single().ModPathModOnly);
        var validated = ManifestValidator.Validate(remote, new HashSet<string>(EnginePresets.Presets.Keys)).Manifest;
        EffectiveManifest.SetRemote(validated);

        var root = Path.Combine(_root, "cp");
        Directory.CreateDirectory(Path.Combine(root, "archive", "pc", "mod"));
        File.WriteAllText(Path.Combine(root, "archive", "pc", "mod", "Orphan.archive.xl"), "XL");
        var g = new GameEntry
        {
            Id = "cyberpunk-2077", GameName = "Cyberpunk 2077", Engine = "custom", SteamAppId = "1091500", GameRoot = root,
            DataDir = Path.Combine(_root, "data"), FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
        Directory.CreateDirectory(g.DataDir!);
        var c = Scanner.GameContext(g);

        Assert.Equal("marked mod-only by the game's definition", ModOnlyFolders.WhyModOnly(c, c.Locations[0]));
        Assert.Contains(RestorePointEngine.PlanVanillaRemainder(c, Array.Empty<ClearSkip>()).Files,
            f => f.Rel.Replace('\\', '/') == "archive/pc/mod/Orphan.archive.xl");
    }
}
