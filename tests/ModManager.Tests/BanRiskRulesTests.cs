using ModManager.Core;

namespace ModManager.Tests;

public class BanRiskRulesTests
{
    [Theory]
    [InlineData("high", GameBanRisk.High)]
    [InlineData("HIGH", GameBanRisk.High)]
    [InlineData("medium", GameBanRisk.Medium)]
    [InlineData("low", GameBanRisk.Low)]
    [InlineData(null, GameBanRisk.None)]
    [InlineData("", GameBanRisk.None)]
    [InlineData("garbage", GameBanRisk.None)]
    public void Parse_maps_strings_case_insensitively(string? s, GameBanRisk expected)
        => Assert.Equal(expected, BanRiskRules.Parse(s));

    [Fact]
    public void Canonical_round_trips_the_levels()
    {
        Assert.Equal("high", BanRiskRules.Canonical(GameBanRisk.High));
        Assert.Equal("medium", BanRiskRules.Canonical(GameBanRisk.Medium));
        Assert.Equal("low", BanRiskRules.Canonical(GameBanRisk.Low));
        Assert.Null(BanRiskRules.Canonical(GameBanRisk.None));
    }

    [Fact]
    public void MaxString_never_downgrades()
    {
        Assert.Equal("high", BanRiskRules.MaxString("high", null));   // remote null can't lower curated high
        Assert.Equal("high", BanRiskRules.MaxString("low", "high"));  // remote high raises
        Assert.Equal("high", BanRiskRules.MaxString("high", "low"));  // remote low can't lower
        Assert.Null(BanRiskRules.MaxString(null, null));
    }

    [Fact]
    public void ShouldGateEnable_only_gates_high_and_unacked()
    {
        Assert.True(BanRiskRules.ShouldGateEnable(GameBanRisk.High, alreadyAcked: false));
        Assert.False(BanRiskRules.ShouldGateEnable(GameBanRisk.High, alreadyAcked: true));
        Assert.False(BanRiskRules.ShouldGateEnable(GameBanRisk.Medium, alreadyAcked: false));
        Assert.False(BanRiskRules.ShouldGateEnable(GameBanRisk.None, alreadyAcked: false));
    }
}
