using ModManager.Core;

namespace ModManager.Tests;

public class SteamBuildCheckTests
{
    [Fact]
    public void Unknown_when_no_live_build() // not a Steam game / no manifest
    {
        Assert.Equal(SteamBuildStatus.Unknown, SteamBuildCheck.Evaluate("123", null));
        Assert.Equal(SteamBuildStatus.Unknown, SteamBuildCheck.Evaluate(null, ""));
    }

    [Fact]
    public void NoBaseline_when_baseline_missing_but_live_present()
        => Assert.Equal(SteamBuildStatus.NoBaseline, SteamBuildCheck.Evaluate(null, "17556649"));

    [Fact]
    public void Unchanged_when_equal()
        => Assert.Equal(SteamBuildStatus.Unchanged, SteamBuildCheck.Evaluate("17556649", "17556649"));

    [Fact]
    public void Updated_when_live_differs_from_baseline()
        => Assert.Equal(SteamBuildStatus.Updated, SteamBuildCheck.Evaluate("17556649", "17600000"));
}
