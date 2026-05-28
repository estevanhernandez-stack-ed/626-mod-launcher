using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointEngineEndStateTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-end-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private (GameEntry game, GameContext c, string modsDir) MakeGame()
    {
        var gameRoot = Path.Combine(_tmp, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot, DataDir = Path.Combine(_tmp, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        };
        return (game, Scanner.GameContext(game), modsDir);
    }

    [Fact]
    public async Task ModsActive_re_enables_all_and_reports_outcomes()
    {
        var (game, c, modsDir) = MakeGame();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "modsActive"), gameArchiveDir);

        var result = RestorePointEngine.ApplyEndState(c, "modsActive", gameArchiveDir);

        Assert.Equal("DATA", File.ReadAllText(Path.Combine(modsDir, "cool.pak")));
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")));
        Assert.Contains(result.EnableOutcomes, o => o.Name == "cool" && o.Enabled);
        Assert.Empty(result.MovedFiles);
    }

    [Fact]
    public void Vanilla_moves_detected_directinject_files_to_archive_and_records_them()
    {
        var (game, c, _) = MakeGame();
        // "modded-regulation" catalog entry: InstallSignatureFiles = ["regulation.bin"].
        // Verified against KnownDirectInjectMod.Catalog — single-file, no dirs, no contains.
        // DirectInject.Detect receives basenames (Path.GetFileName applied by MoveDirectInjectToArchive);
        // a "regulation.bin" in the game root matches and its Entries list contains "regulation.bin".
        File.WriteAllBytes(Path.Combine(c.GameRoot, "regulation.bin"), new byte[] { 1, 2, 3 });
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);

        var result = RestorePointEngine.ApplyEndState(c, "vanilla", gameArchiveDir);

        Assert.NotEmpty(result.MovedFiles);
        foreach (var mf in result.MovedFiles)
            Assert.True(File.Exists(Path.Combine(gameArchiveDir, "vanilla-moved", mf.Rel)),
                $"missing archived {mf.Rel}");
        Assert.All(result.MovedFiles, mf => Assert.False(string.IsNullOrEmpty(mf.Sha256)));
        // The game root file is gone — moved into the archive.
        Assert.False(File.Exists(Path.Combine(c.GameRoot, "regulation.bin")));
    }
}
