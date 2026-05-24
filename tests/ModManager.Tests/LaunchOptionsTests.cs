using ModManager.Core;

namespace ModManager.Tests;

// The verified, gated Launch Options catalog. Internal options the app runs (start the real exe
// offline); external options the user pastes into Steam. Unknown games return nothing — we never
// guess a launch command, we gate it until researched.
public class LaunchOptionsTests
{
    [Fact]
    public void Elden_Ring_has_a_recommended_internal_offline_option()
    {
        var opts = LaunchOptions.For("1245620");
        var offline = Assert.Single(opts);
        Assert.Equal(LaunchOptionKind.Internal, offline.Kind);
        Assert.True(offline.Recommended);                 // highlight: mods need it
        Assert.Contains("eldenring.exe", offline.Exe);     // run the real exe directly (no EAC)
        Assert.Equal("Game", offline.WorkingSubdir);       // FromSoft nests the exe under Game\
        Assert.Contains("online", offline.Detail, StringComparison.OrdinalIgnoreCase); // warns about online
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
