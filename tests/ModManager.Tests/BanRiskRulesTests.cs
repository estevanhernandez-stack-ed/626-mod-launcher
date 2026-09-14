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

    [Theory]
    [InlineData(GameBanRisk.High, false, true)]
    [InlineData(GameBanRisk.High, true, false)]
    [InlineData(GameBanRisk.Medium, false, false)]
    [InlineData(GameBanRisk.Low, false, false)]
    [InlineData(GameBanRisk.None, false, false)]
    public void ShouldGateSaveWrite_gates_only_high_and_unacked(GameBanRisk level, bool acked, bool expected)
        => Assert.Equal(expected, BanRiskRules.ShouldGateSaveWrite(level, acked));

    [Fact]
    public void ShouldGateSaveWrite_asks_every_time_until_acked()
    {
        // No hidden "already shown" state: the prompt comes back on every write until the box is ticked.
        Assert.True(BanRiskRules.ShouldGateSaveWrite(GameBanRisk.High, saveWritesAcked: false));
        Assert.True(BanRiskRules.ShouldGateSaveWrite(GameBanRisk.High, saveWritesAcked: false));
    }
}
