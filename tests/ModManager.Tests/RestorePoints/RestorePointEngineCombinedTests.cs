using ModManager.Core;
using ModManager.Core.Frameworks;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

// SEAM coverage the slice tests leave open: a COMBINED game (disabled pak mod + installed framework
// + root direct-inject file + UE4SS loader mod) through CaptureGame -> ApplyEndState("vanilla") ->
// ReplayGame. Proves the three methods compose to a byte-for-byte restore, including the framework
// double-path (manifest restored via the archived data dir; files restored from frameworks-state).
public class RestorePointEngineCombinedTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-comb-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public async Task Vanilla_round_trip_restores_a_combined_game_byte_for_byte()
    {
        var gameRoot = Path.Combine(_tmp, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        var ue4ssMods = Path.Combine(gameRoot, "ue4ss", "Mods");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(Path.Combine(ue4ssMods, "Cheat"));

        var dataDir = Path.Combine(_tmp, "_626mods", "t");
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot, DataDir = dataDir,
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[]
            {
                new ModLocation("mods", "Mods", "mods"),
                new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" },
            },
            LaunchTargets = new[] { new LaunchTarget("Play", "exe", "game.exe") { IsDefault = true } },
        };
        var c = Scanner.GameContext(game);

        // (1) A disabled pak mod -> lives in the data dir's holding folder.
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "MOD-CONTENT");
        await Scanner.DisableModAsync("cool", c);
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "cool", "cool.pak"))); // pre-condition

        // (2) An installed framework: a dll at the game root + its install.json manifest in the data dir.
        File.WriteAllBytes(Path.Combine(gameRoot, "dinput8.dll"), new byte[] { 10, 20, 30 });
        var fwDir = Path.Combine(dataDir, "frameworks", "elden-mod-loader");
        Directory.CreateDirectory(fwDir);
        var fwManifest = new FrameworkInstallManifest(
            "elden-mod-loader", "Elden Mod Loader", "TechieW",
            gameRoot, new[] { "dinput8.dll" }, DateTime.UtcNow, null);
        var camel = new System.Text.Json.JsonSerializerOptions
        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = true };
        File.WriteAllText(Path.Combine(fwDir, "install.json"),
            System.Text.Json.JsonSerializer.Serialize(fwManifest, camel));

        // (3) A root direct-inject file (modded-regulation signature).
        File.WriteAllBytes(Path.Combine(gameRoot, "regulation.bin"), new byte[] { 1, 2, 3, 4 });

        // (4) A UE4SS loader mod, enabled in the manifest.
        File.WriteAllText(Path.Combine(ue4ssMods, "mods.txt"), "Cheat : 1\n");
        File.WriteAllText(Path.Combine(ue4ssMods, "Cheat", "main.lua"), "-- cheat");
        Assert.True(Ue4ssManifest.IsEnabled(ue4ssMods, "Cheat")); // pre-condition

        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");

        // ---- CAPTURE (non-destructive) ----
        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);

        // Capture must not have touched live state.
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "cool", "cool.pak")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "regulation.bin")));
        Assert.True(File.Exists(Path.Combine(fwDir, "install.json")));
        Assert.Single(entry.Frameworks);
        Assert.Contains(entry.LoaderMods, l => l.Name == "Cheat" && l.Loader == "ue4ss" && l.Enabled);

        // ---- APPLY END-STATE: vanilla ----
        var end = RestorePointEngine.ApplyEndState(c, "vanilla", gameArchiveDir);
        entry = entry with { MovedFiles = end.MovedFiles };

        // Vanilla mutated live: direct-inject moved out, framework files + manifest deleted, loader flipped off.
        Assert.False(File.Exists(Path.Combine(gameRoot, "regulation.bin")));
        Assert.False(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));   // framework Uninstall deleted the file
        Assert.False(File.Exists(Path.Combine(fwDir, "install.json")));     // ...and the LIVE manifest
        Assert.False(Ue4ssManifest.IsEnabled(ue4ssMods, "Cheat"));          // loader flipped off

        // ---- REPLAY ----
        RestorePointEngine.ReplayGame(entry, gameArchiveDir, c);

        // Direct-inject restored byte-for-byte.
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(gameRoot, "regulation.bin")));
        // Framework FILE restored from frameworks-state.
        Assert.Equal(new byte[] { 10, 20, 30 }, File.ReadAllBytes(Path.Combine(gameRoot, "dinput8.dll")));
        // Framework MANIFEST restored from the archived data dir (so a later Uninstall still works).
        Assert.True(File.Exists(Path.Combine(fwDir, "install.json")));
        Assert.Contains("elden-mod-loader", FrameworkRegistry.List(dataDir).Select(f => f.FrameworkId));
        // Loader enable-state re-applied.
        Assert.True(Ue4ssManifest.IsEnabled(ue4ssMods, "Cheat"));
        // Disabled holding mod survived the data-dir copy-back.
        Assert.Equal("MOD-CONTENT", File.ReadAllText(Path.Combine(c.DisabledRoot, "cool", "cool.pak")));
    }

    // Regression: modsActive end-state replay must not produce double-state.
    // ApplyEndState("modsActive") re-enables the disabled mod (moves it OUT of holding into mods/,
    // tears down the holding folder). The archived data dir was copied at capture time WHILE the mod
    // was still disabled, so it carries disabled/cool/. Before the fix, ReplayGame's additive
    // data-dir copy-back resurrected disabled/cool/ — mod ended up in BOTH mods/ and holding.
    // The fix: ReplayGame skips the archived disabled/ sub-tree when end-state is modsActive.
    [Fact]
    public async Task ModsActive_round_trip_does_not_resurrect_the_holding_copy()
    {
        var gameRoot = Path.Combine(_tmp, "game2");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var dataDir = Path.Combine(_tmp, "_626mods", "t2");
        var game = new GameEntry
        {
            Id = "t2", GameName = "T2", GameRoot = gameRoot, DataDir = dataDir,
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "Mods", "mods") },
        };
        var c = Scanner.GameContext(game);

        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "MOD-CONTENT");
        await Scanner.DisableModAsync("cool", c);

        // Pre-conditions: mod is in holding, not in mods/.
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "cool", "cool.pak")));
        Assert.False(File.Exists(Path.Combine(modsDir, "cool.pak")));

        var gameArchiveDir = Path.Combine(_tmp, "archive2", "games", "t2");
        // CaptureGame snapshots the data dir NOW (mod is disabled — archived data carries disabled/cool/).
        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "modsActive"), gameArchiveDir);

        // ApplyEndState re-enables all mods: cool moves to mods/, holding is torn down.
        var end = RestorePointEngine.ApplyEndState(c, "modsActive", gameArchiveDir);
        Assert.Contains(end.EnableOutcomes, o => o.Name == "cool" && o.Enabled);
        Assert.True(File.Exists(Path.Combine(modsDir, "cool.pak")));          // live in mods/
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "cool"))); // holding gone

        // ReplayGame must NOT resurrect disabled/cool/ from the stale archive.
        RestorePointEngine.ReplayGame(entry, gameArchiveDir, c);

        // The mod is in exactly one place: the live mods folder (enabled).
        Assert.True(File.Exists(Path.Combine(modsDir, "cool.pak")),
            "cool.pak must remain live in mods/ after replay.");
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")),
            "disabled/cool/ must NOT be resurrected from the stale archive — that would leave the mod in both mods/ and holding.");
        Assert.Equal("MOD-CONTENT", File.ReadAllText(Path.Combine(modsDir, "cool.pak")));
    }
}
