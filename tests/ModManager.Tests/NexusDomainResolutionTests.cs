using ModManager.Core;

namespace ModManager.Tests;

// Regression (friend report 2026-05-31): a Windrose added via Steam auto-add had an EMPTY
// NexusGameDomain, so md5 metadata identify early-returned "no matches" and backfill refused with
// "This game has no Nexus domain set." Steam auto-add / quick-pick / manual all flow through
// BuildGameEntry but never set the domain — only the AI-profile path did. Fix: BuildGameEntry
// resolves the domain from the Steam app id (NexusDomains.ByAppId) when the input didn't carry one.
public class NexusDomainResolutionTests
{
    [Fact]
    public void NexusDomains_maps_windrose_appid_to_its_slug()
    {
        // appId 3041230 confirmed on disk; slug confirmed by maintainer.
        Assert.Equal("windrose", NexusDomains.ByAppId("3041230"));
    }

    [Fact]
    public void NexusDomains_returns_null_for_unknown_appid()
    {
        Assert.Null(NexusDomains.ByAppId("000000"));
        Assert.Null(NexusDomains.ByAppId(null));
        Assert.Null(NexusDomains.ByAppId(""));
    }

    [Fact]
    public void BuildGameEntry_resolves_nexus_domain_from_app_id_when_input_has_none()
    {
        // This is the exact Steam-auto-add shape: app id present, no explicit NexusGameDomain.
        var input = new GameInput { Name = "Windrose", Engine = "ue-pak", SteamAppId = "3041230" };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Equal("windrose", entry.NexusGameDomain);
    }

    [Fact]
    public void BuildGameEntry_prefers_an_explicit_domain_over_the_app_id_map()
    {
        // An AI profile (or manual override) that names the domain wins, even if the app id maps too.
        var input = new GameInput
        {
            Name = "Windrose", Engine = "ue-pak", SteamAppId = "3041230",
            NexusGameDomain = "windrose-override",
        };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Equal("windrose-override", entry.NexusGameDomain);
    }

    [Fact]
    public void BuildGameEntry_leaves_domain_null_when_app_id_is_unmapped_and_no_explicit_domain()
    {
        var input = new GameInput { Name = "Mystery", Engine = "ue-pak", SteamAppId = "000000" };
        var entry = EnginePresets.BuildGameEntry(input, existingIds: null);
        Assert.Null(entry.NexusGameDomain);
    }
}
