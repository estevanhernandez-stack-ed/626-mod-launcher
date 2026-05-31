using ModManager.Core;

namespace ModManager.Tests;

// UE4SS installs into <project>/Binaries/Win64, which also holds the game exe (<Project>-Win64-Shipping.exe).
// That exe name is per-game, so a literal forbidden entry can't protect it across games. PathGate.IsForbidden
// gains a leading-"*" suffix match — but ONLY for top-level entries (no '/'), so it guards the exe sitting in
// the install root next to the proxy, without banning a same-suffix file nested deeper in the payload.
public class PathGateForbiddenGlobTests
{
    [Fact]
    public void Leading_star_matches_a_top_level_basename_suffix()
    {
        var forbidden = new[] { "*-Shipping.exe" };
        Assert.True(PathGate.IsForbidden("Windrose-Win64-Shipping.exe", forbidden));
        Assert.True(PathGate.IsForbidden("Palworld-Win64-Shipping.exe", forbidden)); // per-game name, same rule
    }

    [Fact]
    public void Leading_star_does_not_match_a_nested_path()
    {
        // A legit mod file deeper in the payload that happens to share the suffix must NOT be refused —
        // the guard protects the exe at the install root, not a filename everywhere.
        var forbidden = new[] { "*-Shipping.exe" };
        Assert.False(PathGate.IsForbidden("ue4ss/Mods/Cool-Shipping.exe", forbidden));
    }

    [Fact]
    public void Leading_star_does_not_match_an_unrelated_top_level_file()
    {
        var forbidden = new[] { "*-Shipping.exe" };
        Assert.False(PathGate.IsForbidden("dwmapi.dll", forbidden));
        Assert.False(PathGate.IsForbidden("UE4SS.dll", forbidden));
    }

    [Fact]
    public void Literal_forbidden_entries_still_match_exactly_as_before()
    {
        // The existing ELM behavior must be untouched: exact basename + full-relative-path matching.
        var forbidden = new[] { "eldenring.exe" };
        Assert.True(PathGate.IsForbidden("eldenring.exe", forbidden));
        Assert.True(PathGate.IsForbidden("Game/eldenring.exe", forbidden)); // basename match at any depth (unchanged)
        Assert.False(PathGate.IsForbidden("eldenring.exe.bak", forbidden));
    }
}
