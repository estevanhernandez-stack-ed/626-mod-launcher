using ModManager.Core;

namespace ModManager.Tests;

// The verified, gated Launch Options catalog. Internal options the app runs (start the real exe
// offline); external options the user pastes into Steam. Unknown games return nothing — we never
// guess a launch command, we gate it until researched.
public class LaunchOptionsTests
{
    [Fact]
    public void Elden_Ring_has_a_recommended_anticheat_toggle()
    {
        var opts = LaunchOptions.For("1245620");
        var ac = Assert.Single(opts);
        Assert.Equal(LaunchOptionKind.AntiCheatToggle, ac.Kind);
        Assert.True(ac.Recommended);                          // highlight: mods need it
        Assert.Equal("start_protected_game.exe", ac.Bootstrapper); // the EAC bootstrapper to swap
        Assert.Equal("eldenring.exe", ac.RealExe);            // the real exe swapped in
        Assert.Equal("Game", ac.WorkingSubdir);               // FromSoft nests the exe under Game\
        Assert.Contains("online", ac.Detail, StringComparison.OrdinalIgnoreCase); // warns about online
    }

    [Fact]
    public void Unknown_or_unverified_games_return_nothing()
    {
        Assert.Empty(LaunchOptions.For("999999"));
        Assert.Empty(LaunchOptions.For(null));
    }

    [Fact]
    public void NeedsLaunchOption_flags_a_game_with_a_recommended_option()
    {
        Assert.True(LaunchOptions.NeedsAttention("1245620"));
        Assert.False(LaunchOptions.NeedsAttention("999999"));
    }
}
