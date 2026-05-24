using ModManager.Core;

namespace ModManager.Tests;

// Ports name-match-core.test.js — clean a filename into a search query, then score
// CurseForge hits by token overlap and refuse weak matches (no wrong-metadata attach).
public class NameMatchTests
{
    private sealed record Cand(string Name);

    [Fact]
    public void CleanModName_strips_P_suffix_loadorder_tags_and_multipliers()
    {
        Assert.Equal("BLACK MARKET SHIPYARD", NameMatch.CleanModName("ZZZ.CF.JSON.AL_BLACK_MARKET_SHIPYARD_P"));
        Assert.Equal("Cool Mod", NameMatch.CleanModName("AAA_Cool_Mod"));
        Assert.Equal("Strength Parry", NameMatch.CleanModName("StrengthParry_P"));
        Assert.Equal("More Stacks", NameMatch.CleanModName("MoreStacks_10x"));
        Assert.Equal("No Fog Of War", NameMatch.CleanModName("NoFogOfWar_v2"));
    }

    [Fact]
    public void CleanModName_drops_a_known_file_extension()
    {
        Assert.Equal("No Fog of War", NameMatch.CleanModName("No_Fog_of_War.pak"));
    }

    [Fact]
    public void PickBestMatch_returns_closest_candidate_above_threshold()
    {
        var cands = new[] { new Cand("Black Market Shipyard"), new Cand("Some Other Mod") };
        Assert.Equal("Black Market Shipyard", NameMatch.PickBestMatch("black market shipyard", cands, c => c.Name)!.Name);
    }

    [Fact]
    public void PickBestMatch_tolerates_residual_noise_tokens()
    {
        var cands = new[] { new Cand("Black Market Shipyard") };
        Assert.Equal("Black Market Shipyard", NameMatch.PickBestMatch("json black market shipyard", cands, c => c.Name)!.Name);
    }

    [Fact]
    public void PickBestMatch_returns_null_when_nothing_clears_threshold()
    {
        Assert.Null(NameMatch.PickBestMatch("black market shipyard", new[] { new Cand("Totally Unrelated Thing") }, c => c.Name));
        Assert.Null(NameMatch.PickBestMatch("anything", Array.Empty<Cand>(), c => c.Name));
    }
}
