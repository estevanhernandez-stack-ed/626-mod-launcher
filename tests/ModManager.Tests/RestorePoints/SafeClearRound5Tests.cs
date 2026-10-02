using System.Diagnostics;
using System.Security.Cryptography;
using ManifestMiner;
using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 5 (review r4): the <c>modPathModOnly</c> flag is bound to the path it describes and gated like a
/// trust-sensitive field (I-1); vanilla turns off paks-root and outside-root mods again (I-2); in a folder the
/// allowlist doesn't vouch for, it still turns off what an install record says 626 placed (Este's ruling);
/// the sheet says in counts what happened; and the five minors, including a junction planted after the clear
/// that let Restore write into <c>bin\</c>.
/// </summary>
public class SafeClearRound5Tests : IDisposable
{
    private const string Ts = "20261002-180000";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-r5-" + Guid.NewGuid().ToString("n"));
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

    private static readonly HashSet<string> Engines = new(StringComparer.OrdinalIgnoreCase)
        { "bethesda", "ue-pak", "custom", "fromsoft", "minecraft" };

    private static GameManifestEntry Validated(GameManifestEntry e)
        => ManifestValidator.Validate(new GameManifest { Games = new[] { e } }, Engines).Manifest.Games.Single();

    // ---- I-1: the flag's trust ----

    [Fact]
    public void D1_a_feed_flagging_Skyrims_Data_loses_the_flag_and_keeps_the_rest()
    {
        var feed = new GameManifestEntry
        {
            Id = "skyrim-se", Name = "Skyrim SE", Engine = "bethesda", ModPath = "Data", ModPathModOnly = true,
            BanRisk = "low", Stores = new StoreIds { SteamAppId = "489830" },
        };
        var result = ManifestValidator.Validate(new GameManifest { Games = new[] { feed } }, Engines);

        var kept = result.Manifest.Games.Single();
        Assert.Null(kept.ModPathModOnly);
        Assert.Equal("Data", kept.ModPath);           // the rest of the entry still applies
        Assert.Equal("low", kept.BanRisk);
        Assert.Contains("skyrim-se", result.DroppedModOnlyFlags);
    }

    [Theory]
    [InlineData("Data Files")]
    [InlineData("data")]
    [InlineData("Modules")]
    [InlineData("GameData")]
    [InlineData("Win64/ovldata")]
    [InlineData("nativePC")]
    [InlineData("natives")]
    [InlineData("Vampire")]
    [InlineData("TBL/Content/Paks")]
    [InlineData("Content/Paks")]
    [InlineData(".")]
    [InlineData("./")]
    public void The_flag_is_dropped_on_the_root_and_every_known_base_content_shape(string modPath)
        => Assert.Null(Validated(new GameManifestEntry { Id = "g", Engine = "custom", ModPath = modPath, ModPathModOnly = true }).ModPathModOnly);

    [Theory]
    [InlineData("archive/pc/mod")]
    [InlineData("XComGame/Mods")]
    [InlineData("Data/Mods")]
    [InlineData("reframework/autorun")]
    [InlineData("Game/Content/Paks/~mods")]
    public void The_flag_is_kept_on_a_plausible_mod_folder(string modPath)
        => Assert.True(Validated(new GameManifestEntry { Id = "g", Engine = "custom", ModPath = modPath, ModPathModOnly = true }).ModPathModOnly);

    [Fact]
    public void D2_a_flag_with_no_mod_path_is_dropped_and_can_never_attach_to_the_snapshots_path()
    {
        var feed = new GameManifestEntry { Id = "skyrim-se", Engine = "bethesda", ModPathModOnly = true };
        var validated = ManifestValidator.Validate(new GameManifest { Games = new[] { feed } }, Engines);
        Assert.Null(validated.Manifest.Games.Single().ModPathModOnly);
        Assert.Contains("skyrim-se", validated.DroppedModOnlyFlags);

        // Even ungated, the merge binds the flag to its own side's path: a flag-only remote is ignored.
        var embedded = new GameManifest { Games = new[] { new GameManifestEntry { Id = "skyrim-se", Engine = "bethesda", ModPath = "Data" } } };
        var merged = EffectiveManifest.Merge(embedded, new GameManifest { Games = new[] { feed } }).Games.Single();
        Assert.Equal("Data", merged.ModPath);
        Assert.Null(merged.ModPathModOnly);
    }

    [Fact]
    public void D3b_a_feed_correcting_Cyberpunks_mod_path_does_not_inherit_the_snapshots_true()
    {
        var embedded = new GameManifest { Games = new[] { new GameManifestEntry { Id = "cyberpunk-2077", ModPath = "archive/pc/mod", ModPathModOnly = true } } };
        var remote = new GameManifest { Games = new[] { new GameManifestEntry { Id = "cyberpunk-2077", ModPath = "archive/pc/content" } } };

        var merged = EffectiveManifest.Merge(embedded, remote).Games.Single();

        Assert.Equal("archive/pc/content", merged.ModPath);
        Assert.Null(merged.ModPathModOnly);   // a path change drops the flag unless the feed restates it
    }

    [Fact]
    public void A_feed_restating_both_the_path_and_the_flag_keeps_it()
    {
        var embedded = new GameManifest { Games = new[] { new GameManifestEntry { Id = "g", ModPath = "a/mods" } } };
        var remote = new GameManifest { Games = new[] { new GameManifestEntry { Id = "g", ModPath = "b/mods", ModPathModOnly = true } } };
        Assert.True(EffectiveManifest.Merge(embedded, remote).Games.Single().ModPathModOnly);
    }

    [Fact]
    public void The_miner_refuses_a_curated_flag_on_a_base_content_path()
    {
        var problems = OverridesValidate.Check(new[]
        {
            new OverrideEntry { SteamAppId = "489830", Id = "skyrim-se", Name = "Skyrim", Engine = "bethesda", ModPath = "Data", ModPathModOnly = true },
        });
        Assert.Contains(problems, p => p.Message.Contains("modPathModOnly") && p.Message.Contains("own content"));
    }

    // ---- I-2: paks-root and outside-root mods are turned off again ----

    [Fact]
    public async Task A_paks_root_mod_is_turned_off_and_comes_back_and_the_base_pak_is_untouched()
    {
        var root = Path.Combine(_root, "ue");
        var paks = Path.Combine(root, "Game", "Content", "Paks");
        Put(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak"), "BASE");
        Put(Path.Combine(paks, "CoolMod_P.pak"), "COOL");
        var g = new GameEntry
        {
            Id = "ue", GameName = "UE", Engine = "ue-pak", GameRoot = root, DataDir = DataDir("ue"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Paks", "Game/Content/Paks") { Form = "paks-root" } },
        };
        var before = Snapshot(root);
        var orch = Make(g);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.False(File.Exists(Path.Combine(paks, "CoolMod_P.pak")));
        Assert.Equal("BASE", File.ReadAllText(Path.Combine(paks, "pakchunk0-WindowsNoEditor.pak")));
        Assert.Contains(RestorePointManifestStore.Read(RpDir)!.Games[0].TurnedOffByClear!, m => m.Name == "CoolMod_P");

        var restore = await orch.RestoreAsync(Ts, default);
        Assert.True(restore.Ok);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, root);
    }

    // (The outside-root case is SafeClearAllowlistTests.A_mod_folder_outside_the_game_is_never_swept_but_its_mods_turn_off_and_come_back.)

    // ---- Ownership: in an unvouched folder, what 626 placed is still turned off ----

    private static void Record(GameEntry g, string location, params string[] files)
        => ModInstallRegistry.Save(g.DataDir!, new ModInstallManifest(
            ModInstallRegistry.IdFor(files[0] + ".zip"), files[0] + ".zip", location, files, DateTime.UtcNow));

    [Fact]
    public async Task On_a_hand_set_Cyberpunk_folder_a_626_installed_mod_turns_off_and_back_and_an_unrecorded_one_stays()
    {
        var root = Path.Combine(_root, "cp");
        var mods = Path.Combine(root, "archive", "pc", "mod");
        Put(Path.Combine(mods, "CoolMod.archive"), "COOL");
        Put(Path.Combine(mods, "Sideloaded.archive"), "SIDE");
        var g = new GameEntry
        {
            Id = "cyberpunk-2077", GameName = "Cyberpunk 2077", Engine = "custom", SteamAppId = "1091500", GameRoot = root,
            DataDir = DataDir("cp"), FileExtensions = new[] { "archive" },
            UserSet = new List<string> { GameEntry.UserSetModLocations },   // set by hand: the definition's flag doesn't apply
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
        Record(g, "mods", "CoolMod.archive");
        var before = Snapshot(root);
        var orch = Make(g);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.False(File.Exists(Path.Combine(mods, "CoolMod.archive")));              // placed by 626: off
        Assert.Equal("SIDE", File.ReadAllText(Path.Combine(mods, "Sideloaded.archive")));   // no record: left on
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Contains(ga.TurnedOffByClear!, m => m.Name == "CoolMod");
        Assert.DoesNotContain(ga.TurnedOffByClear!, m => m.Name == "Sideloaded");
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "Sideloaded" && n.Reason.StartsWith("still active"));

        var restore = await orch.RestoreAsync(Ts, default);
        Assert.True(restore.Ok);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, root);
    }

    [Fact]
    public async Task On_Skyrims_Data_a_626_installed_plugin_turns_off_and_back_and_Skyrim_esm_is_untouched()
    {
        var root = Path.Combine(_root, "skyrim");
        var data = Path.Combine(root, "Data");
        Put(Path.Combine(data, "Skyrim.esm"), "MASTER");
        Put(Path.Combine(data, "Skyrim - Textures0.bsa"), "BSA");
        Put(Path.Combine(data, "SomeMod.esp"), "MOD");
        var g = new GameEntry
        {
            Id = "skyrim", GameName = "Skyrim", Engine = "bethesda", GameRoot = root, DataDir = DataDir("skyrim"),
            FileExtensions = new[] { "esp", "esl", "esm", "bsa" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "Data") },
        };
        Record(g, "mods", "SomeMod.esp");
        var before = Snapshot(root);
        var orch = Make(g);

        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.False(File.Exists(Path.Combine(data, "SomeMod.esp")));
        Assert.Equal("MASTER", File.ReadAllText(Path.Combine(data, "Skyrim.esm")));
        Assert.Equal("BSA", File.ReadAllText(Path.Combine(data, "Skyrim - Textures0.bsa")));
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Equal(new[] { "SomeMod" }, ga.TurnedOffByClear!.Select(m => m.Name));
        Assert.Empty(ga.VanillaRemainder!);   // Data is never swept

        var restore = await orch.RestoreAsync(Ts, default);
        Assert.True(restore.Ok);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, root);
    }

    // ---- Sheet wording, in counts ----

    private static GameArchive Vanilla(int off, int stillActive, int moved) => new(
        "t", "T", @"D:\T", "vanilla", Array.Empty<LaunchTarget>(), null,
        Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(), Array.Empty<OwnedModNote>(),
        Array.Empty<MovedFile>(), Array.Empty<ArchivedMod>(), null,
        TurnedOffByClear: Enumerable.Range(0, off).Select(i => new ClearedMod("Off" + i, "mods")).ToList(),
        VanillaRemainder: Enumerable.Range(0, moved).Select(i => new MovedFile("mods/f" + i, 1, "x")).ToList(),
        // Mods still active (round 6 counts only rows that are mods: here, ones that replaced a game file).
        LeftInPlace: Enumerable.Range(0, stillActive).Select(i => new InPlaceNote("On" + i, RestorePointEngine.ReplacedGameFileNote))
            .Append(new InPlaceNote("Data", "626 can't tell the game's own files from mods in Data; 3 files no mod claims are still in place"))
            .ToList());

    [Fact]
    public void The_sheet_says_zero_when_nothing_was_turned_off()
    {
        var line = OffBoardingHydrator.Hydrate(Vanilla(0, 3, 0), "rp").LaunchLines.Single();
        Assert.StartsWith("626 didn't turn off any mods here: 3 mods are still active", line);
        Assert.DoesNotContain("turned off its mods", line);
        Assert.DoesNotContain("cleared its mod folders", line);
    }

    [Fact]
    public void The_sheet_says_how_many_of_how_many_when_only_some_were_turned_off()
    {
        var line = OffBoardingHydrator.Hydrate(Vanilla(2, 1, 5), "rp").LaunchLines.Single();
        Assert.StartsWith("626 turned off 2 of 3 mods; 1 is still active, and moved 5 other files", line);
        Assert.Contains("may not be fully vanilla", line);
    }

    [Fact]
    public void The_sheet_says_all_when_every_mod_was_turned_off_but_files_remain()
    {
        var line = OffBoardingHydrator.Hydrate(Vanilla(4, 0, 0), "rp").LaunchLines.Single();
        Assert.StartsWith("626 turned off all 4 mods it found", line);
        Assert.Contains("STILL IN PLACE", line);
    }

    // ---- m1: the fromsoft shape is ME2's folder, not any folder named mod ----

    [Theory]
    [InlineData("Game/data/mod")]
    [InlineData("a/b/mod")]
    public void m1_a_fromsoft_mod_folder_deeper_than_one_level_is_not_mod_only(string rel)
        => Assert.False(ModOnlyFolders.IsShape("fromsoft", rel));

    [Fact]
    public void m1_with_a_Mod_Engine_2_config_only_the_folder_beside_it_is_mod_only()
    {
        var root = Path.Combine(_root, "er");
        Directory.CreateDirectory(Path.Combine(root, "ModEngine2", "mod"));
        Directory.CreateDirectory(Path.Combine(root, "other", "mod"));
        Put(Path.Combine(root, "ModEngine2", "config_eldenring.toml"), "mods = [\n]\n");
        GameContext Ctx(string modPath) => Scanner.GameContext(new GameEntry
        {
            Id = "er", GameName = "ER", Engine = "fromsoft", GameRoot = root, DataDir = DataDir("er"),
            ModEngineConfig = Path.Combine(root, "ModEngine2", "config_eldenring.toml"),
            ModLocations = new[] { new ModLocation("mods", "Mods", modPath) { Form = "folders" } },
        });

        var me2 = Ctx("ModEngine2/mod");
        Assert.NotNull(ModOnlyFolders.WhyModOnly(me2, me2.Locations[0]));
        var other = Ctx("other/mod");
        Assert.Null(ModOnlyFolders.WhyModOnly(other, other.Locations[0]));
    }

    // ---- m2: a base pak in a ue-pak Mods folder is never turned off or swept ----

    [Fact]
    public async Task m2_base_paks_in_a_ue_Mods_folder_stay_while_the_mod_goes()
    {
        var root = Path.Combine(_root, "uemods");
        var mods = Path.Combine(root, "Game", "Content", "Paks", "Mods");
        Put(Path.Combine(mods, "pakchunk0-Windows.pak"), "BASE");
        Put(Path.Combine(mods, "pakchunk0-Windows.sig"), "BASE-SIG");
        Put(Path.Combine(mods, "global.ucas"), "GUCAS");
        Put(Path.Combine(mods, "global.utoc"), "GUTOC");
        Put(Path.Combine(mods, "CoolMod.pak"), "COOL");
        var g = new GameEntry
        {
            Id = "uem", GameName = "UEM", Engine = "ue-pak", GameRoot = root, DataDir = DataDir("uem"),
            FileExtensions = new[] { "pak", "ucas", "utoc" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "Game/Content/Paks/Mods") },
        };

        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);

        Assert.False(File.Exists(Path.Combine(mods, "CoolMod.pak")));
        foreach (var (f, v) in new[] { ("pakchunk0-Windows.pak", "BASE"), ("pakchunk0-Windows.sig", "BASE-SIG"), ("global.ucas", "GUCAS"), ("global.utoc", "GUTOC") })
            Assert.Equal(v, File.ReadAllText(Path.Combine(mods, f)));
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.DoesNotContain(ga.TurnedOffByClear!, m => m.Name is "pakchunk0-Windows" or "global");
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "pakchunk0-Windows" && n.Reason.StartsWith("still active"));
        Assert.False(OffBoardingHydrator.FullyVanilla(ga));
    }

    // ---- m3: a junction planted after the clear can't carry Restore's write out of the mod folder ----

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

    [Fact]
    public async Task m3_a_junction_planted_after_the_clear_is_refused_at_restore_and_bin_is_untouched()
    {
        var root = Path.Combine(_root, "mc");
        var mods = Path.Combine(root, "mods");
        var bin = Path.Combine(root, "bin");
        Put(Path.Combine(mods, "lnk", "through.dll"), "THROUGH");
        Put(Path.Combine(mods, "keep.cfg"), "KEEP");
        Directory.CreateDirectory(bin);
        var g = new GameEntry
        {
            Id = "mc", GameName = "MC", Engine = "minecraft", GameRoot = root, DataDir = DataDir("mc"),
            FileExtensions = new[] { "jar" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var orch = Make(g);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        Assert.False(Directory.Exists(Path.Combine(mods, "lnk")) && File.Exists(Path.Combine(mods, "lnk", "through.dll")));
        if (Directory.Exists(Path.Combine(mods, "lnk"))) Directory.Delete(Path.Combine(mods, "lnk"), true);
        Assert.True(TryJunction(Path.Combine(mods, "lnk"), bin), "mklink /J failed");

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.False(File.Exists(Path.Combine(bin, "through.dll")));
        Assert.Contains(restore.Warnings, w => w.Contains("through.dll") && w.Contains("link"));
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(mods, "keep.cfg")));   // the rest came back
        Directory.Delete(Path.Combine(mods, "lnk"));
    }

    // ---- m4: a remainder file Restore can't place says where it is ----

    [Fact]
    public async Task m4_a_remainder_left_behind_because_the_location_changed_names_where_it_is()
    {
        var root = Path.Combine(_root, "mc4");
        Put(Path.Combine(root, "mods", "loose.cfg"), "LOOSE");
        var g = new GameEntry
        {
            Id = "mc4", GameName = "MC4", Engine = "minecraft", GameRoot = root, DataDir = DataDir("mc4"),
            FileExtensions = new[] { "jar" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var orch = Make(g);
        Assert.True((await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        g.Engine = "custom";   // the user changed the engine between the clear and the restore

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok);
        Assert.Contains(restore.Warnings, w => w.Contains("loose.cfg")
            && w.Contains(Path.Combine(RpDir, "games", "mc4", RestorePointEngine.RemainderDirName)));
    }

    // ---- m5: the pre-flight counts proxy-loader holds too ----

    [Fact]
    public void m5_the_pre_flight_counts_a_proxy_loaders_hold()
    {
        var root = Path.Combine(_root, "px");
        Put(Path.Combine(root, "version.dll"), "PROXY-DLL");   // 9 bytes, stepped aside from the play folder
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        var g = new GameEntry
        {
            Id = "px", GameName = "PX", Engine = "minecraft", GameRoot = root, DataDir = DataDir("px"),
            FileExtensions = new[] { "jar" }, ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var c = Scanner.GameContext(g);
        Assert.Contains(ModListing.Resolve(g), m => m.Location == ProxyLoaderRows.LocationTag);

        Assert.Equal(9, RestorePointEngine.EstimateTurnOffBytes(c, outsideModOnlyOnly: true));
    }
}
