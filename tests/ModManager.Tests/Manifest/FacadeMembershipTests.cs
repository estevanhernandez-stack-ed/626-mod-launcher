using ModManager.Core;

namespace ModManager.Tests.Manifest;

// The manifest is a UNION of three arrays with different memberships. These tests guard against
// a game leaking from one facade into another now that they share one backing store.
public class FacadeMembershipTests
{
    [Theory]
    [InlineData("3156770")] // Witchfire — nexus-domains only
    [InlineData("3041230")] // Windrose — nexus-domains only
    [InlineData("1091500")] // Cyberpunk — nexus-domains + popular-games, never known-engines
    [InlineData("294100")]  // RimWorld — popular-games only
    public void KnownEngines_does_not_leak_non_known_engine_games(string appId)
        => Assert.Null(KnownEngines.ByAppId(appId));
}
