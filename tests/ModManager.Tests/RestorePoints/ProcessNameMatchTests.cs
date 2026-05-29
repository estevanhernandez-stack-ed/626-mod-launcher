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

    private static GameEntry FromSoftGame(params (string kind, string target)[] targets)
    {
        var g = Game(targets);
        g.Engine = "fromsoft";
        return g;
    }

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

    // --- Engine runtime exe: a bootstrapper launch target exits after spawning the game, so the
    // live process is the engine's runtime exe, which is never a LaunchTarget. ---

    [Fact]
    public void Matches_engine_runtime_exe_when_only_bootstrapper_is_a_launch_target()
    {
        // ER's only exe target is the Seamless bootstrapper (exits after spawning); during play
        // the live process is eldenring.exe, which is not a target.
        var g = FromSoftGame(("exe", @"D:\ELDEN RING\Game\ersc_launcher.exe"), ("steam", "steam://rungameid/1245620"));
        Assert.True(ProcessNameMatch.AnyRunning(g, new[] { "explorer", "eldenring" }));
    }

    [Fact]
    public void Matches_eac_bootstrapper_runtime_for_fromsoft()
    {
        var g = FromSoftGame(("steam", "steam://rungameid/1245620"));
        Assert.True(ProcessNameMatch.AnyRunning(g, new[] { "start_protected_game" }));
    }

    [Fact]
    public void No_engine_runtime_match_for_unknown_engine()
    {
        // Non-fromsoft (engine null): only the exe launch target is checked, no runtime-exe fallback.
        var g = Game(("exe", @"D:\x\game.exe"));
        Assert.False(ProcessNameMatch.AnyRunning(g, new[] { "eldenring", "start_protected_game" }));
    }
}
