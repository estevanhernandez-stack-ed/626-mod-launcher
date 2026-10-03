using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Frameworks;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Round 10 (/code-review on #385):
/// <list type="bullet">
/// <item>Restore must see the game AFTER its own replay. The clear swept <c>ue4ss/Mods</c> while UE4SS was
/// registered and then uninstalled UE4SS; a context built before replay has no <c>ue4ss-mods</c> location,
/// so every Lua mod file was refused and the loader states never applied.</item>
/// <item>Rows the clear never touched because of WHERE they are (another tool's folder, a system folder, a
/// drive-relative location) are "left alone" on the sheet, never "turned off by 626".</item>
/// </list>
/// </summary>
public class SafeClearRound10Tests : IDisposable
{
    private const string Ts = "20261003-120000";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-r10-" + Guid.NewGuid().ToString("n"));
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

    // ---- 1. Restore sees the replayed game: a UE4SS game's Lua mods come back ----

    [Fact]
    public async Task A_UE4SS_games_Lua_mods_come_back_byte_identical_after_a_vanilla_clear_and_restore()
    {
        var root = Path.Combine(_root, "Palworld");
        var win64 = Path.Combine(root, "Pal", "Binaries", "Win64");
        var luaMods = Path.Combine(win64, "ue4ss", "Mods");
        Put(Path.Combine(win64, "Palworld-Win64-Shipping.exe"), "EXE");
        Put(Path.Combine(win64, "dwmapi.dll"), "UE4SS-PROXY");
        Put(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "UE4SS");
        // CRLF, as UE4SS ships it and as Ue4ssManifest writes it, so a flip off and back on is byte-identical.
        Put(Path.Combine(luaMods, "mods.txt"), "CoolLua : 1\r\nOtherLua : 1\r\n");
        Put(Path.Combine(luaMods, "CoolLua", "Scripts", "main.lua"), "-- cool");
        Put(Path.Combine(luaMods, "OtherLua", "Scripts", "main.lua"), "-- other");
        Put(Path.Combine(root, "Pal", "Content", "Paks", "~mods", "Pak_P.pak"), "PAK");
        var g = new GameEntry
        {
            Id = "palworld", GameName = "Palworld", Engine = "ue-pak", GameRoot = root, DataDir = DataDir("palworld"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "Pal/Content/Paks/~mods") },
        };
        var fwDir = Path.Combine(g.DataDir!, "frameworks", "ue4ss");
        Directory.CreateDirectory(fwDir);
        File.WriteAllText(Path.Combine(fwDir, "install.json"), JsonSerializer.Serialize(
            new FrameworkInstallManifest("ue4ss", "UE4SS", "UE4SS team", win64, new[] { "dwmapi.dll", "ue4ss/UE4SS.dll" }, DateTime.UtcNow, null),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        // Pre-condition: the launcher's UE4SS location is there, with both Lua mods on.
        var c0 = Scanner.GameContext(g);
        Assert.Contains(c0.Locations, l => l.Name == ModOnlyFolders.Ue4ssAutoLocationName);
        Assert.True(Ue4ssManifest.IsEnabled(luaMods, "CoolLua"));
        var before = Snapshot(root);
        var orch = Make(g);

        var clear = await orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default);

        Assert.True(clear.Ok, clear.RefusedReason);
        Assert.False(File.Exists(Path.Combine(luaMods, "CoolLua", "Scripts", "main.lua")));   // swept
        Assert.False(File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll")));                // uninstalled
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        Assert.Contains(ga.VanillaRemainder!, f => f.Rel.Replace('\\', '/').EndsWith("ue4ss/Mods/CoolLua/Scripts/main.lua"));

        var restore = await orch.RestoreAsync(Ts, default);

        Assert.True(restore.Ok, restore.RefusedReason);
        Assert.True(restore.Warnings.Count == 0, string.Join(" | ", restore.Warnings));
        AssertSameTree(before, root);
        Assert.True(Ue4ssManifest.IsEnabled(luaMods, "CoolLua"));
        Assert.True(Ue4ssManifest.IsEnabled(luaMods, "OtherLua"));
    }

    // ---- 2. Rows left alone because of where they are ----

    private static string StateOfRow(GameArchive ga, string name)
        => OffBoardingHydrator.Hydrate(ga, "rp").Mods.Single(m => m.Name == name).State!;

    [Fact]
    public async Task A_Vortex_managed_row_is_left_alone_never_turned_off_by_626()
    {
        var root = Path.Combine(_root, "vtx");
        Put(Path.Combine(root, "mods", "Mine.pak"), "MINE");
        Put(Path.Combine(root, "vortex", "Owned.pak"), "OWNED");
        Put(Path.Combine(root, "vortex", "vortex.deployment.pak.json"), "{}");
        var g = new GameEntry
        {
            Id = "vtx", GameName = "Vtx", Engine = "minecraft", GameRoot = root, DataDir = DataDir("vtx"),
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods"), new ModLocation("vortex", "Vortex", "vortex") },
        };
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];

        Assert.Equal(OffBoardingModState.TurnedOff, StateOfRow(ga, "Mine"));
        Assert.Equal(OffBoardingModState.LeftAlone, StateOfRow(ga, "Owned"));
        var sheet = OffBoardingSheet.Render(OffBoardingHydrator.Hydrate(ga, "rp"));
        var off = sheet.IndexOf("Turned off by 626", StringComparison.Ordinal);
        var alone = sheet.IndexOf("Left alone (626 didn't touch these)", StringComparison.Ordinal);
        Assert.True(off >= 0 && alone > off, sheet);
        Assert.True(sheet.IndexOf("    Owned", StringComparison.Ordinal) > alone, sheet);
        Assert.Contains("managed by Vortex", sheet[alone..]);
    }

    [Fact]
    public async Task A_row_in_a_system_folder_location_is_left_alone()
    {
        // The ancestor of the game folder is a system-folder location on every OS (no special folders needed).
        var common = Path.Combine(_root, "steamapps", "common");
        var root = Path.Combine(common, "TheGame");
        Put(Path.Combine(root, "game.exe"), "EXE");
        Put(Path.Combine(common, "Loose.pak"), "LOOSE");
        var g = new GameEntry
        {
            Id = "anc", GameName = "Anc", Engine = "custom", GameRoot = root, DataDir = DataDir("anc"),
            FileExtensions = new[] { "pak" }, ModLocations = new[] { new ModLocation("mods", "Mods", common) },
        };
        Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
        var ga = RestorePointManifestStore.Read(RpDir)!.Games[0];

        Assert.Equal("LOOSE", File.ReadAllText(Path.Combine(common, "Loose.pak")));
        Assert.Equal(OffBoardingModState.LeftAlone, StateOfRow(ga, "Loose"));
    }

    [WindowsFact]
    public async Task A_row_in_a_drive_relative_location_is_left_alone()
    {
        // "C:" means "the current folder on C:". The test's working directory may be anywhere, so give the
        // scan something to list by pointing the process there for the duration.
        var cwdDir = Path.Combine(_root, "cwd");
        Put(Path.Combine(cwdDir, "Stray.pak"), "STRAY");
        var root = Path.Combine(_root, "dr");
        Put(Path.Combine(root, "game.exe"), "EXE");
        var g = new GameEntry
        {
            Id = "dr", GameName = "Dr", Engine = "custom", GameRoot = root, DataDir = DataDir("dr"),
            FileExtensions = new[] { "pak" },
            ModLocations = new[] { new ModLocation("mods", "Mods", Path.GetPathRoot(cwdDir)!.TrimEnd('\\')) },
        };
        var old = Environment.CurrentDirectory;
        GameArchive ga;
        try
        {
            Environment.CurrentDirectory = cwdDir;
            Assert.True((await Make(g).SafeClearAsync(new SafeClearOptions { DefaultEndState = "vanilla" }, Ts, default)).Ok);
            ga = RestorePointManifestStore.Read(RpDir)!.Games[0];
        }
        finally { Environment.CurrentDirectory = old; }

        Assert.Equal("STRAY", File.ReadAllText(Path.Combine(cwdDir, "Stray.pak")));
        Assert.Empty(ga.TurnedOffByClear!);
        if (ga.Mods.Any(m => m.Name == "Stray")) Assert.Equal(OffBoardingModState.LeftAlone, StateOfRow(ga, "Stray"));
        Assert.Contains(ga.LeftInPlace!, n => n.Path == "Stray" && n.Reason.StartsWith(RestorePointEngine.LeftAlonePrefix));
    }
}
