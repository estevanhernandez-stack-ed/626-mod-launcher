using ModManager.Core;

namespace ModManager.Tests;

public class SteamInstallStateTests
{
    [Theory]
    [InlineData("4")]     // StateFullyInstalled
    [InlineData("6")]     // 4|2 — installed AND update-required (still installed; do NOT hide)
    [InlineData("1542")]  // bit 4 set among several flags (observed on a real library)
    public void IsFullyInstalled_true_when_fully_installed_bit_set(string flags)
        => Assert.True(SteamInstallState.IsFullyInstalled(flags));

    [Theory]
    [InlineData("1")]     // StateUninstalled
    [InlineData("2")]     // update-required, not installed
    [InlineData("1026")]  // 1024|2 — downloading, bit 4 clear
    public void IsFullyInstalled_false_when_fully_installed_bit_clear(string flags)
        => Assert.False(SteamInstallState.IsFullyInstalled(flags));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notanumber")]
    public void IsFullyInstalled_defaults_to_true_when_unknown(string? flags) // never hide a real game on uncertainty
        => Assert.True(SteamInstallState.IsFullyInstalled(flags));
}
