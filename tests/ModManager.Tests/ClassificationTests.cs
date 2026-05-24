using ModManager.Core;

namespace ModManager.Tests;

// Ports classification-core.test.js — MP/SP auto-seed (mirrored->both, client-only->sp,
// existing choices win) and the mode-enable filter.
public class ClassificationTests
{
    [Fact]
    public void Seed_mirrored_both_clientonly_sp_keep_existing()
    {
        var existing = new Dictionary<string, string> { ["keep"] = "mp" };
        var mods = new (string Name, bool OnServer)[] { ("a", true), ("b", false), ("keep", true) };

        var outMap = Classification.Seed(existing, mods);

        Assert.Equal("both", outMap["a"]);
        Assert.Equal("sp", outMap["b"]);
        Assert.Equal("mp", outMap["keep"]);
    }

    [Theory]
    [InlineData("all", "sp", true)]
    [InlineData("mp", "sp", false)]
    [InlineData("mp", "both", true)]
    [InlineData("mp", "mp", true)]
    [InlineData("sp", "mp", false)]
    [InlineData("sp", "both", true)]
    public void ModeFilter_rules(string mode, string cls, bool expected)
    {
        Assert.Equal(expected, Classification.ModeFilter(mode, cls));
    }
}
