using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

/// <summary>
/// Vanilla's turn-offs and Restore's turn-ons run through one <c>Scanner.BulkScope</c> each, so a large
/// Cyberpunk-shaped library pays a constant number of mod-list and extra-tree reads, not one per mod.
/// Counted with <see cref="ScanCostProbe"/>, never timed: the same operation on 10 mods and on 40 must cost
/// the same.
/// </summary>
public class SafeClearBulkCostTests : IDisposable
{
    private static readonly string[] Trees = { "r6/scripts", "r6/tweaks" };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rp-cost-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        ScanCostProbe.Current = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static void Put(string abs, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, content);
    }

    private (GameEntry game, GameContext c) TreeGame(int mods)
    {
        var root = Path.Combine(_root, "g" + mods);
        for (var i = 0; i < mods; i++)
        {
            Put(Path.Combine(root, "archive", "pc", "mod", $"Mod{i:000}.archive"), "A" + i);
            Put(Path.Combine(root, "r6", "scripts", $"Mod{i:000}", "main.reds"), "S" + i);
        }
        var game = new GameEntry
        {
            Id = "g" + mods, GameName = "G", Engine = "custom", SteamAppId = "1091500", GameRoot = root,
            DataDir = Path.Combine(_root, "_626mods", "g" + mods),
            FileExtensions = new[] { "archive" },
            ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        };
        Directory.CreateDirectory(game.DataDir!);
        return (game, Scanner.GameContext(game, extraModTrees: Trees));
    }

    private static (int ModLists, int Trees) Count(Action op)
    {
        var probe = new ScanCostProbe();
        ScanCostProbe.Current = probe;
        try { op(); }
        finally { ScanCostProbe.Current = null; }
        return (probe.ModListBuilds, probe.TreeBuilds);
    }

    private ((int, int) Off, (int, int) On) Measure(int mods)
    {
        var (game, c) = TreeGame(mods);
        var dir = Path.Combine(_root, "archive" + mods, "games", game.Id);
        var ga = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), dir);
        var moves = RestorePointEngine.PlanVanillaMoves(c);
        var set = RestorePointEngine.PlanVanillaTurnOffs(c, moves);
        Assert.Equal(mods, set.Count);

        EndStateResult? end = null;
        var off = Count(() => end = RestorePointEngine.ApplyEndState(c, "vanilla", dir, moves, set));
        Assert.Empty(end!.TurnOffSkips);
        Assert.False(File.Exists(Path.Combine(game.GameRoot, "r6", "scripts", "Mod000", "main.reds")));

        ga = ga with { MovedFiles = moves, TurnedOffByClear = set };
        ReplayResult? replay = null;
        var on = Count(() => replay = RestorePointEngine.ReplayGame(ga, dir, c));
        Assert.Empty(replay!.NotBackOn);
        Assert.Equal("S0", File.ReadAllText(Path.Combine(game.GameRoot, "r6", "scripts", "Mod000", "main.reds")));
        return (off, on);
    }

    [Fact]
    public void Vanilla_turn_offs_and_restore_turn_ons_cost_the_same_for_10_mods_and_40()
    {
        var small = Measure(10);
        var large = Measure(40);

        Assert.Equal(small.Off, large.Off);
        Assert.Equal(small.On, large.On);
    }
}
