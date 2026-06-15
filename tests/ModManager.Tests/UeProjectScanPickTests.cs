using ModManager.Core;

namespace ModManager.Tests;

public class UeProjectScanPickTests
{
    private static UeProjectCandidate C(string rel, int depth, bool pak = false, bool bin = false, bool upr = false)
        => new(rel, depth, HasShippingPak: pak, HasBinariesSibling: bin, HasUprojectSibling: upr);

    [Fact]
    public void Empty_is_none()
        => Assert.Equal(UeProjectPickKind.None, UeProjectScan.Pick(Array.Empty<UeProjectCandidate>()).Kind);

    [Fact]
    public void Single_candidate_is_chosen_even_without_signals()
    {
        var pick = UeProjectScan.Pick(new[] { C("Pal", 1) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Pal", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Two_unremarkable_candidates_are_ambiguous()
    {
        var pick = UeProjectScan.Pick(new[] { C("A", 1), C("B", 1) });
        Assert.Equal(UeProjectPickKind.Ambiguous, pick.Kind);
    }

    [Fact]
    public void One_project_looking_candidate_beats_an_unremarkable_one()
    {
        var pick = UeProjectScan.Pick(new[] { C("Tool", 1), C("Game", 1, bin: true) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Game", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Two_project_looking_candidates_tie_is_ambiguous()
    {
        var pick = UeProjectScan.Pick(new[] { C("Game", 1, bin: true), C("Other", 1, bin: true) });
        Assert.Equal(UeProjectPickKind.Ambiguous, pick.Kind);
    }

    [Fact]
    public void Client_beats_server_when_both_look_like_projects()
    {
        var pick = UeProjectScan.Pick(new[] { C("GameServer", 1, bin: true), C("Game", 1, bin: true) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Game", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Shallower_wins_among_project_looking()
    {
        var pick = UeProjectScan.Pick(new[] { C("Outer/Inner", 2, pak: true), C("Shallow", 1, pak: true) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("Shallow", pick.Chosen!.RelativeProjectPath);
    }

    [Fact]
    public void Uproject_sibling_breaks_a_tie_among_equal_project_candidates()
    {
        // Both project-looking (Binaries), same depth, both non-server — identical except the .uproject.
        var pick = UeProjectScan.Pick(new[] { C("GameA", 1, bin: true), C("GameB", 1, bin: true, upr: true) });
        Assert.Equal(UeProjectPickKind.One, pick.Kind);
        Assert.Equal("GameB", pick.Chosen!.RelativeProjectPath); // the +5 .uproject tie-breaker decides it
    }

    [Fact]
    public void Denylist_includes_engine_and_anticheat()
    {
        Assert.Contains("Engine", UeProjectScan.Denylist);
        Assert.Contains("EasyAntiCheat", UeProjectScan.Denylist);
    }
}
