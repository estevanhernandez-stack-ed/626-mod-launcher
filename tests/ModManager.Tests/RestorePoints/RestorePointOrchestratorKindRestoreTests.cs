using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Frameworks;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Restoring a kind point (the Patch Day snapshot, ledger D8 / item 12). It is an OVERLAY, not a Safe Clear
/// replay: it never touches a loader mod's enable state, never writes a framework onto a ban-risk game, never
/// puts back a framework that is not installed now, never hands back consent or a Vortex hand-over, and it
/// takes a "beforeRestore" snapshot of the current state first, so the restore is itself undoable.
/// </summary>
public class RestorePointOrchestratorKindRestoreTests : IDisposable
{
    private const string Cap = "20261016-120000";
    private const string Snap1 = "20261016-130000";
    private const string Snap2 = "20261016-140000";
    private static readonly DateTime Installed = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-kind-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string DataRoot => Path.Combine(_root, "appdata");

    private sealed class FakeNexus : INexusGate { public bool IsConnected => true; public void DeleteStoredKey() { } }
    private sealed class FakeProbe : IGameRunningProbe
    {
        public bool Running { get; set; }
        public bool AnyRunning(GameEntry g) => Running;
    }
    private sealed class FakeProvider(IEnumerable<GameEntry> games) : IGameProvider
    {
        private readonly List<GameEntry> _games = games.ToList();
        public int ReloadCalls { get; private set; }
        public IReadOnlyList<GameEntry> Games => _games;
        public GameContext ContextFor(GameEntry g) => Scanner.GameContext(g);
        public void Reload() => ReloadCalls++;
    }

    private sealed record Fx(GameEntry Game, RestorePointOrchestrator Orch, FakeProbe Probe, FakeProvider Prov,
        string Root, string Win64, string LuaMods, string Data);

    private static void Put(string abs, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    private static Dictionary<string, string> Snapshot(string dir)
        => !Directory.Exists(dir)
            ? new Dictionary<string, string>()
            : Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(dir, p),
                    p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.OrdinalIgnoreCase);

    private static void RegisterFramework(GameEntry g, string id, string installPath, string[] files, DateTime stamp)
    {
        var dir = Path.Combine(g.DataDir!, "frameworks", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "install.json"), JsonSerializer.Serialize(
            new FrameworkInstallManifest(id, id.ToUpperInvariant(), "team", installPath, files, stamp, null),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static readonly string[] Ue4ssFiles =
        { "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini", "ue4ss/Mods/mods.txt" };

    // A UE4SS game. CoolLua is OFF and OtherLua ON at capture; UE4SS is registered; metadata.json is v1.
    private Fx Ue4ss(string? steamAppId = null)
    {
        var root = Path.Combine(_root, "Palworld");
        var win64 = Path.Combine(root, "Pal", "Binaries", "Win64");
        var luaMods = Path.Combine(win64, "ue4ss", "Mods");
        Put(Path.Combine(win64, "Palworld-Win64-Shipping.exe"), "EXE");
        Put(Path.Combine(win64, "dwmapi.dll"), "PROXY-V1");
        Put(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "UE4SS-V1");
        Put(Path.Combine(win64, "ue4ss", "UE4SS-settings.ini"), "cfg=1");
        Put(Path.Combine(luaMods, "mods.txt"), "CoolLua : 0\r\nOtherLua : 1\r\n");
        Put(Path.Combine(luaMods, "CoolLua", "Scripts", "main.lua"), "-- cool");
        Put(Path.Combine(luaMods, "OtherLua", "Scripts", "main.lua"), "-- other");
        var data = Path.Combine(DataRoot, "_626mods", "palworld");
        Directory.CreateDirectory(data);
        var g = new GameEntry
        {
            Id = "palworld", GameName = "Palworld", Engine = "ue-pak", GameRoot = root, DataDir = data, SteamAppId = steamAppId,
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "Pal/Content/Paks/~mods") },
        };
        Directory.CreateDirectory(Path.Combine(root, "Pal", "Content", "Paks", "~mods"));
        RegisterFramework(g, "ue4ss", win64, Ue4ssFiles, Installed);
        Put(Path.Combine(data, "metadata.json"), "{\"v\":1}");
        return Wire(g, root, win64, luaMods, data);
    }

    private Fx Wire(GameEntry g, string root, string win64, string luaMods, string data)
    {
        var prov = new FakeProvider(new[] { g });
        var probe = new FakeProbe();
        var orch = new RestorePointOrchestrator(DataRoot, Path.Combine(DataRoot, "restore-points"), "0.25.0", prov, new FakeNexus(), probe);
        return new Fx(g, orch, probe, prov, root, win64, luaMods, data);
    }

    private async Task<string> Capture(Fx fx)
    {
        var cap = await fx.Orch.CaptureGameOnlyAsync(fx.Game.Id, RestorePointKinds.PatchDay, Cap, default);
        Assert.True(cap.Ok, cap.RefusedReason);
        return cap.RestorePointTimestamp!;
    }

    // The player carries on after the capture: CoolLua is switched ON (with its enabled.txt), OtherLua OFF, a
    // framework file is edited, the proxy is replaced, and metadata moves on.
    private static void CarryOn(Fx fx)
    {
        Put(Path.Combine(fx.LuaMods, "mods.txt"), "CoolLua : 1\r\nOtherLua : 0\r\n");
        Put(Path.Combine(fx.LuaMods, "CoolLua", "enabled.txt"), "");
        Put(Path.Combine(fx.Win64, "dwmapi.dll"), "PROXY-V2");
        Put(Path.Combine(fx.Win64, "ue4ss", "UE4SS-settings.ini"), "cfg=2");
        Put(Path.Combine(fx.Data, "metadata.json"), "{\"v\":2}");
    }

    private string Points => Path.Combine(DataRoot, "restore-points");

    // ---- the headline: enable states, enabled.txt, loader plugins ----

    [Fact]
    public async Task Restoring_a_kind_point_puts_framework_files_and_metadata_back_and_leaves_every_enable_state_alone()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        CarryOn(fx);
        var rootBefore = Snapshot(fx.Root);
        var luaBefore = Snapshot(fx.LuaMods);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        // Enable states: exactly as the player left them. mods.txt (which is also one of the framework's own
        // files) and enabled.txt are not rewritten, so there is nothing to undo and nothing was deleted.
        Assert.Equal(luaBefore, Snapshot(fx.LuaMods));
        Assert.True(File.Exists(Path.Combine(fx.LuaMods, "CoolLua", "enabled.txt")));
        Assert.True(Ue4ssManifest.IsEnabled(fx.LuaMods, "CoolLua"));
        Assert.False(Ue4ssManifest.IsEnabled(fx.LuaMods, "OtherLua"));
        // What the overlay is for: the framework's files and launcher metadata, as captured.
        Assert.Equal("PROXY-V1", File.ReadAllText(Path.Combine(fx.Win64, "dwmapi.dll")));
        Assert.Equal("cfg=1", File.ReadAllText(Path.Combine(fx.Win64, "ue4ss", "UE4SS-settings.ini")));
        Assert.Equal("{\"v\":1}", File.ReadAllText(Path.Combine(fx.Data, "metadata.json")));
        // Nothing else in the game folder moved, and no temp file was left behind.
        var rootAfter = Snapshot(fx.Root);
        Assert.Equal(rootBefore.Keys.OrderBy(k => k), rootAfter.Keys.OrderBy(k => k));
        foreach (var (rel, sha) in rootBefore)
            if (!rel.EndsWith("dwmapi.dll") && !rel.EndsWith("UE4SS-settings.ini")) Assert.Equal(sha, rootAfter[rel]);
        Assert.DoesNotContain(Directory.GetFiles(fx.Data, "*", SearchOption.AllDirectories), f => f.EndsWith(".rp-tmp"));
    }

    [Fact]
    public async Task A_bepinex_plugin_enabled_since_capture_stays_enabled_and_the_framework_comes_back()
    {
        var root = Path.Combine(_root, "Valheim");
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        Put(Path.Combine(plugins, "Early.dll"), "EARLY");
        Put(Path.Combine(plugins, "Later.dll.disabled"), "LATER");
        Put(Path.Combine(root, "winhttp.dll"), "WINHTTP-V1");
        Put(Path.Combine(root, "BepInEx", "core", "BepInEx.dll"), "CORE-V1");
        var data = Path.Combine(DataRoot, "_626mods", "valheim");
        Directory.CreateDirectory(data);
        var g = new GameEntry
        {
            Id = "valheim", GameName = "Valheim", Engine = "bepinex", GameRoot = root, DataDir = data,
            FileExtensions = new[] { "dll" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "plugins", "BepInEx/plugins") },
        };
        RegisterFramework(g, "bepinex", root, new[] { "winhttp.dll", "BepInEx/core/BepInEx.dll" }, Installed);
        var fx = Wire(g, root, root, plugins, data);
        var ts = await Capture(fx);

        File.Move(Path.Combine(plugins, "Later.dll.disabled"), Path.Combine(plugins, "Later.dll"));   // on since capture
        File.Move(Path.Combine(plugins, "Early.dll"), Path.Combine(plugins, "Early.dll.disabled"));   // off since capture
        Put(Path.Combine(root, "winhttp.dll"), "WINHTTP-V2");
        Put(Path.Combine(root, "BepInEx", "core", "BepInEx.dll"), "CORE-V2");
        var pluginsBefore = Snapshot(plugins);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal(pluginsBefore, Snapshot(plugins));
        Assert.True(BepInExPlugins.IsEnabled(plugins, "Later"));
        Assert.False(BepInExPlugins.IsEnabled(plugins, "Early"));
        Assert.Equal("WINHTTP-V1", File.ReadAllText(Path.Combine(root, "winhttp.dll")));
        Assert.Equal("CORE-V1", File.ReadAllText(Path.Combine(root, "BepInEx", "core", "BepInEx.dll")));
    }

    // ---- the restore is itself undoable ----

    [Fact]
    public async Task A_beforeRestore_snapshot_is_listed_holds_the_pre_restore_framework_and_restoring_it_goes_back()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        CarryOn(fx);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);
        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal("PROXY-V1", File.ReadAllText(Path.Combine(fx.Win64, "dwmapi.dll")));

        var snapName = $"{Snap1}-palworld";
        var listed = fx.Orch.ListRestorePoints();
        Assert.Equal(2, listed.Count);
        Assert.Equal(RestorePointKinds.PatchDay, listed.Single(p => p.Timestamp == ts).Kind);
        Assert.Equal(RestorePointKinds.BeforeRestore, listed.Single(p => p.Timestamp == snapName).Kind);
        Assert.Contains(r.Warnings, w => w.Contains(snapName));   // the player is told where the way back is

        var rp = Path.Combine(Points, snapName);
        var m = RestorePointManifestStore.Read(rp)!;
        Assert.Equal(RestorePointKinds.BeforeRestore, m.Kind);
        var fw = Assert.Single(Assert.Single(m.Games).Frameworks);
        Assert.Equal("PROXY-V2", File.ReadAllText(Path.Combine(rp, "games", "palworld", fw.CapturedStateRel!, "dwmapi.dll")));
        Assert.Equal("cfg=2", File.ReadAllText(Path.Combine(rp, "games", "palworld", fw.CapturedStateRel!, "ue4ss", "UE4SS-settings.ini")));
        Assert.Equal(Installed, fw.InstalledUtc);

        // And back: the pre-restore bytes return, the enable states are still the player's, and that restore
        // took a snapshot of its own (a restore of a restore point is never a one-way door).
        var luaBefore = Snapshot(fx.LuaMods);
        var back = await fx.Orch.RestoreAsync(snapName, default, Snap2);

        Assert.True(back.Ok, back.RefusedReason);
        Assert.Equal("PROXY-V2", File.ReadAllText(Path.Combine(fx.Win64, "dwmapi.dll")));
        Assert.Equal("cfg=2", File.ReadAllText(Path.Combine(fx.Win64, "ue4ss", "UE4SS-settings.ini")));
        Assert.Equal("{\"v\":2}", File.ReadAllText(Path.Combine(fx.Data, "metadata.json")));
        Assert.Equal(luaBefore, Snapshot(fx.LuaMods));
        Assert.Equal(3, fx.Orch.ListRestorePoints().Count);
    }

    // ---- ban risk ----

    [Fact]
    public async Task On_an_unacknowledged_ban_risk_game_frameworks_are_not_written_and_nothing_is_enabled()
    {
        var fx = Ue4ss(steamAppId: "4032350");
        var ts = await Capture(fx);
        CarryOn(fx);
        var rootBefore = Snapshot(fx.Root);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal(rootBefore, Snapshot(fx.Root));   // not one framework byte, not one enable state
        Assert.Contains(r.Warnings, w => w.Contains("ban risk", StringComparison.OrdinalIgnoreCase));
        Assert.False(BanRiskAckStore.IsAcked(fx.Data, "palworld"));   // and no consent was invented
        Assert.Equal("{\"v\":1}", File.ReadAllText(Path.Combine(fx.Data, "metadata.json")));   // launcher data still restored
    }

    [Fact]
    public async Task On_an_acknowledged_ban_risk_game_the_frameworks_come_back()
    {
        var fx = Ue4ss(steamAppId: "4032350");
        var ts = await Capture(fx);
        CarryOn(fx);
        BanRiskAckStore.Ack(fx.Data, "palworld");

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal("PROXY-V1", File.ReadAllText(Path.Combine(fx.Win64, "dwmapi.dll")));
        Assert.True(Ue4ssManifest.IsEnabled(fx.LuaMods, "CoolLua"));
    }

    // ---- what the overlay leaves alone ----

    [Fact]
    public async Task Consent_and_the_Vortex_hand_over_are_never_restored()
    {
        var fx = Ue4ss();
        BanRiskAckStore.Ack(fx.Data, "palworld");                       // acknowledged when the snapshot was taken
        Put(Path.Combine(fx.Data, "taken-over.json"), "OLD-HANDOVER");
        var ts = await Capture(fx);
        Put(Path.Combine(fx.Data, "ban-risk-acks.json"), "[]");         // the player revoked it since
        Put(Path.Combine(fx.Data, "taken-over.json"), "NEW-HANDOVER");  // and handed a folder back to Vortex
        Put(Path.Combine(fx.Data, "ban-risk-save-acks.json"), "[]");

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.False(BanRiskAckStore.IsAcked(fx.Data, "palworld"));     // an old yes does not become a new yes
        Assert.Equal("[]", File.ReadAllText(Path.Combine(fx.Data, "ban-risk-acks.json")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(fx.Data, "ban-risk-save-acks.json")));
        Assert.Equal("NEW-HANDOVER", File.ReadAllText(Path.Combine(fx.Data, "taken-over.json")));
    }

    [Fact]
    public async Task Data_that_describes_the_game_as_it_is_now_is_not_overlaid_and_nothing_held_comes_back()
    {
        var fx = Ue4ss();
        Put(Path.Combine(fx.Data, "installs", "a.json"), "OLD-INSTALL");
        Put(Path.Combine(fx.Data, "vanilla-stash.json"), "OLD-STASH");
        Put(Path.Combine(fx.Data, "disabled", "Held", "x.pak"), "HELD");
        Put(Path.Combine(fx.Data, "disabled-trees", "Tree", "t.bin"), "TREE");
        var ts = await Capture(fx);
        Put(Path.Combine(fx.Data, "installs", "a.json"), "NEW-INSTALL");
        Put(Path.Combine(fx.Data, "vanilla-stash.json"), "NEW-STASH");
        Directory.Delete(Path.Combine(fx.Data, "disabled"), recursive: true);        // turned back on since
        Directory.Delete(Path.Combine(fx.Data, "disabled-trees"), recursive: true);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal("NEW-INSTALL", File.ReadAllText(Path.Combine(fx.Data, "installs", "a.json")));
        Assert.Equal("NEW-STASH", File.ReadAllText(Path.Combine(fx.Data, "vanilla-stash.json")));
        Assert.False(Directory.Exists(Path.Combine(fx.Data, "disabled")));            // no phantom held copy
        Assert.False(Directory.Exists(Path.Combine(fx.Data, "disabled-trees")));
    }

    [Fact]
    public async Task A_framework_uninstalled_since_capture_is_not_put_back()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        FrameworkRegistry.Uninstall(fx.Data, "ue4ss", fx.Root);
        Assert.False(File.Exists(Path.Combine(fx.Win64, "dwmapi.dll")));

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.False(File.Exists(Path.Combine(fx.Win64, "dwmapi.dll")));
        Assert.False(File.Exists(Path.Combine(fx.Win64, "ue4ss", "UE4SS.dll")));
        Assert.Empty(FrameworkRegistry.List(fx.Data));                                 // not registered again either
        Assert.Contains(r.Warnings, w => w.Contains("UE4SS") && w.Contains("isn't installed"));
    }

    [Fact]
    public async Task A_framework_reinstalled_since_capture_is_left_alone_so_two_versions_never_mix()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        RegisterFramework(fx.Game, "ue4ss", fx.Win64, Ue4ssFiles, Installed.AddDays(3));   // updated through the launcher
        Put(Path.Combine(fx.Win64, "dwmapi.dll"), "PROXY-V3");
        var rootBefore = Snapshot(fx.Root);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal(rootBefore, Snapshot(fx.Root));
        Assert.Contains(r.Warnings, w => w.Contains("UE4SS") && w.Contains("since"));
    }

    // ---- refusals touch nothing ----

    [Fact]
    public async Task A_safe_clear_lock_refuses_the_restore_and_touches_nothing()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        CarryOn(fx);
        var lockPath = Path.Combine(DataRoot, SafeClearLock.FileName);
        File.WriteAllText(lockPath, "20261001-000000");
        var root = Snapshot(fx.Root); var data = Snapshot(fx.Data); var points = Snapshot(Points);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.False(r.Ok);
        Assert.Contains("reset", r.RefusedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(root, Snapshot(fx.Root));
        Assert.Equal(data, Snapshot(fx.Data));
        Assert.Equal(points, Snapshot(Points));                                        // not even a snapshot
        Assert.Equal("20261001-000000", File.ReadAllText(lockPath));
    }

    [Fact]
    public async Task If_the_pre_restore_snapshot_cannot_be_taken_the_restore_is_refused_and_nothing_changes()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        CarryOn(fx);
        fx.Orch.SpaceCheckForTests = (path, payload) =>
            SpaceCheck.Evaluate(Path.GetPathRoot(Path.GetFullPath(path))!, payload, availableBytes: 0);
        var root = Snapshot(fx.Root); var data = Snapshot(fx.Data); var points = Snapshot(Points);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.False(r.Ok);
        Assert.Contains("nothing was restored", r.RefusedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("free space", r.RefusedReason!);
        Assert.Equal(root, Snapshot(fx.Root));
        Assert.Equal(data, Snapshot(fx.Data));
        Assert.Equal(points, Snapshot(Points));
        Assert.Single(fx.Orch.ListRestorePoints());
    }

    [Fact]
    public async Task A_running_game_refuses_the_restore_and_touches_nothing()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        CarryOn(fx);
        fx.Probe.Running = true;
        var root = Snapshot(fx.Root); var data = Snapshot(fx.Data); var points = Snapshot(Points);

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.False(r.Ok);
        Assert.Contains("Palworld", r.RefusedReason!);
        Assert.Equal(root, Snapshot(fx.Root));
        Assert.Equal(data, Snapshot(fx.Data));
        Assert.Equal(points, Snapshot(Points));
    }

    // ---- Safe Clear stays exactly as it was ----

    [Fact]
    public async Task A_kind_restore_is_not_a_reset_so_it_does_not_reload_the_registry_or_clear_the_undo_offer()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        RestoreMarkers.WriteLastClear(DataRoot, "20261001-000000", "20261001-000000");
        var reloads = fx.Prov.ReloadCalls;

        var r = await fx.Orch.RestoreAsync(ts, default, Snap1);

        Assert.True(r.Ok, r.RefusedReason);
        Assert.Equal(reloads, fx.Prov.ReloadCalls);
        Assert.NotNull(RestoreMarkers.ReadLastClear(DataRoot));
    }

    [Fact]
    public async Task A_safe_clear_point_still_restores_while_a_lock_is_on_disk_the_interrupted_clear_path()
    {
        var root = Path.Combine(_root, "pak");
        Put(Path.Combine(root, "mods", "live.pak"), "LIVE");
        var data = Path.Combine(DataRoot, "_626mods", "pak");
        Directory.CreateDirectory(data);
        var g = new GameEntry
        {
            Id = "pak", GameName = "Pak", GameRoot = root, DataDir = data,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
        var fx = Wire(g, root, root, Path.Combine(root, "mods"), data);
        Assert.True((await fx.Orch.SafeClearAsync(new SafeClearOptions { DefaultEndState = "modsActive" }, Cap, default)).Ok);
        File.WriteAllText(Path.Combine(DataRoot, SafeClearLock.FileName), Cap);

        var r = await fx.Orch.RestoreAsync(Cap, default);

        Assert.True(r.Ok, r.RefusedReason);
    }

    // ---- the atomic write ----

    [Fact]
    public void A_failed_atomic_copy_leaves_the_old_file_whole_and_no_temp_behind()
    {
        var src = Path.Combine(_root, "src.bin"); var dest = Path.Combine(_root, "dest.bin");
        Put(src, "NEW"); Put(dest, "OLD");

        using (new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None))   // held open: the swap cannot land
            Assert.ThrowsAny<Exception>(() => RestorePointEngine.CopyFileAtomic(src, dest, verifyHash: true));

        Assert.Equal("OLD", File.ReadAllText(dest));
        Assert.False(File.Exists(dest + ".rp-tmp"));
        RestorePointEngine.CopyFileAtomic(src, dest, verifyHash: true);                 // and it works once free
        Assert.Equal("NEW", File.ReadAllText(dest));
        Assert.False(File.Exists(dest + ".rp-tmp"));
    }

    [Fact]
    public async Task The_framework_install_stamp_is_recorded_in_camelCase()
    {
        var fx = Ue4ss();
        var ts = await Capture(fx);
        var json = File.ReadAllText(Path.Combine(Points, ts, RestorePointManifestStore.FileName));
        Assert.Contains("\"installedUtc\"", json);
        Assert.DoesNotContain("\"InstalledUtc\"", json);
        Assert.Equal(Installed, RestorePointManifestStore.Read(Path.Combine(Points, ts))!.Games[0].Frameworks[0].InstalledUtc);
    }
}
