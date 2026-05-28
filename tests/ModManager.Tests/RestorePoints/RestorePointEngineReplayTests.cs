using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointEngineReplayTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-replay-" + Guid.NewGuid().ToString("n"));
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
    public void Vanilla_then_replay_restores_the_moved_game_folder_file()
    {
        var (game, c, _) = MakeGame();
        // regulation.bin is a real direct-inject catalog signature (modded-regulation).
        var reg = Path.Combine(c.GameRoot, "regulation.bin");
        File.WriteAllBytes(reg, new byte[] { 9, 9, 9 });
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");

        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);
        var end = RestorePointEngine.ApplyEndState(c, "vanilla", gameArchiveDir);
        entry = entry with { MovedFiles = end.MovedFiles };
        Assert.False(File.Exists(reg)); // moved out by vanilla

        RestorePointEngine.ReplayGame(entry, gameArchiveDir, c);

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(reg)); // restored byte-for-byte
    }

    [Fact]
    public void Replay_refuses_a_traversal_movedfile_and_leaves_game_folder_untouched()
    {
        var (game, c, _) = MakeGame();
        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);

        var evil = new GameArchive("t", "T", c.GameRoot, "vanilla",
            game.LaunchTargets, null, Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(),
            Array.Empty<OwnedModNote>(),
            new[] { new MovedFile(@"..\..\Windows\System32\evil.dll", 3, null) },
            Array.Empty<ArchivedMod>(), null);
        // Stage the payload the manifest claims so the ONLY thing stopping the write is PathGate.
        var staged = Path.GetFullPath(Path.Combine(gameArchiveDir, "vanilla-moved", @"..\..\Windows\System32\evil.dll"));
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllBytes(staged, new byte[] { 6, 6, 6 });

        Assert.ThrowsAny<Exception>(() => RestorePointEngine.ReplayGame(evil, gameArchiveDir, c));
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(c.GameRoot, @"..\..\Windows\System32\evil.dll"))));
    }
}
