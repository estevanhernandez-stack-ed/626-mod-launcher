using ModManager.Core;

namespace ModManager.Tests;

// F-015 (vibe-glow wave 4): the mod list gets find-by-name. The predicate is pure Core;
// the App binds it to a filter box in the MODS bar.
public class ModSearchTests
{
    [Theory]
    [InlineData("Faster Ships", "Kingtology", "faster", true)]
    [InlineData("Faster Ships", "Kingtology", "SHIPS", true)]
    [InlineData("Faster Ships", "Kingtology", "kingt", true)]
    [InlineData("Faster Ships", "Kingtology", "slower", false)]
    [InlineData("Faster Ships", "Kingtology", "", true)]
    [InlineData("Faster Ships", "Kingtology", "   ", true)]
    [InlineData("Faster Ships", null, "king", false)]
    [InlineData("MoreStacks (10x)", "Ice Box Studio", "morestacks", true)]
    public void Matches_name_or_author_case_insensitively(string name, string? author, string query, bool expected)
        => Assert.Equal(expected, ModSearch.Matches(name, author, query));

    [Fact]
    public void Null_query_matches_everything()
        => Assert.True(ModSearch.Matches("Anything", "Anyone", null));
}
