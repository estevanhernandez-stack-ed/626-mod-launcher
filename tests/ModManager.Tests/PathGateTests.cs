using ModManager.Core;

namespace ModManager.Tests;

public class PathGateTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pg-root"));

    [Theory]
    [InlineData("Game/dinput8.dll", true)]
    [InlineData("a/b/c.txt", true)]
    [InlineData("../../Windows/System32/x.dll", false)]
    [InlineData("./x", false)]
    [InlineData("", false)]
    [InlineData("C:/x", false)]
    public void IsContained_accepts_inside_rejects_escapes(string rel, bool expected)
        => Assert.Equal(expected, PathGate.IsContained(rel, Root));

    [Fact]
    public void IsForbidden_matches_basename_and_full_relative_case_insensitively()
    {
        var forbidden = new[] { "eldenring.exe", "Game/regulation.bin" };
        Assert.True(PathGate.IsForbidden("Game/ELDENRING.EXE", forbidden));   // basename match
        Assert.True(PathGate.IsForbidden("game/regulation.bin", forbidden));  // full-relative match
        Assert.False(PathGate.IsForbidden("Game/mod.dll", forbidden));
    }

    [Fact]
    public void SafeRelative_strips_wrapper_and_rejects_traversal()
    {
        Assert.Equal(Path.Combine("inner", "f.dll"), PathGate.SafeRelative("wrap/inner/f.dll", "wrap"));
        Assert.Null(PathGate.SafeRelative("wrap/../escape.dll", "wrap"));
        Assert.Null(PathGate.SafeRelative("dir/", null));   // directory entry
        Assert.Null(PathGate.SafeRelative("C:/evil.dll", null));   // drive-rooted entry
    }

    [Fact]
    public void IsContained_normalizes_backslash_form_input()
        => Assert.True(PathGate.IsContained("Game\\dinput8.dll", Root));

    [Fact]
    public void IsForbidden_matches_backslash_style_forbidden_entry()
    {
        var forbidden = new[] { "Binaries\\protected.dll" };
        Assert.True(PathGate.IsForbidden("Binaries/protected.dll", forbidden));   // normalized full-relative match
        Assert.True(PathGate.IsForbidden("Binaries\\protected.dll", forbidden));  // both backslash
    }

    [Fact]
    public void IsContainedAbsolute_rejects_sibling_prefix_bypass()
    {
        var root = Path.Combine(Path.GetTempPath(), "Games", "Elden");
        Assert.True(PathGate.IsContainedAbsolute(Path.Combine(root, "Game", "x.dll"), root));
        Assert.False(PathGate.IsContainedAbsolute(
            Path.Combine(Path.GetTempPath(), "Games", "EldenRingMalware", "x.dll"), root));   // sibling, not inside
    }
}
