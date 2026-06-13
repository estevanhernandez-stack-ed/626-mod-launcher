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

    [Theory]
    [InlineData("374320")]  // Dark Souls III — known-engines only, no Nexus slug
    [InlineData("814380")]  // Sekiro — known-engines only
    [InlineData("1888160")] // Armored Core VI — known-engines only
    [InlineData("294100")]  // RimWorld — popular-games only, no Nexus slug
    public void NexusDomains_does_not_leak_games_without_a_slug(string appId)
        => Assert.Null(NexusDomains.ByAppId(appId));
}
