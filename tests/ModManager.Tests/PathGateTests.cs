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
    }
}
