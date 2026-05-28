using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class ProcessNameMatchTests
{
    private static GameEntry Game(params (string kind, string target)[] targets) => new()
    {
        Id = "t", GameName = "T",
        LaunchTargets = targets.Select(t => new LaunchTarget("L", t.kind, t.target)).ToList(),
    };

    [Fact]
    public void Matches_when_an_exe_launch_target_name_is_running()
    {
        var g = Game(("exe", @"D:\ELDEN RING\Game\eldenring.exe"), ("steam", "steam://rungameid/1245620"));
        Assert.True(ProcessNameMatch.AnyRunning(g, new[] { "explorer", "eldenring" }));  // process names: no ext, case-insensitive
    }

    [Fact]
    public void No_match_when_no_exe_target_is_running()
    {
        var g = Game(("exe", @"D:\x\game.exe"), ("steam", "steam://x"));
        Assert.False(ProcessNameMatch.AnyRunning(g, new[] { "explorer", "discord" }));
    }

    [Fact]
    public void Ignores_non_exe_targets_and_handles_no_targets()
    {
        Assert.False(ProcessNameMatch.AnyRunning(Game(("steam", "steam://x")), new[] { "anything" }));
        Assert.False(ProcessNameMatch.AnyRunning(Game(), new[] { "anything" }));
    }

    [Fact]
    public void Case_insensitive_match()
    {
        var g = Game(("exe", @"D:\x\EldenRing.exe"));
        Assert.True(ProcessNameMatch.AnyRunning(g, new[] { "ELDENRING" }));
    }
}
