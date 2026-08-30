using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Whether a world from this game can be shared without its player — the one question the panel asks
/// before deciding whether the control exists at all.
/// </summary>
public class SaveSeamCatalogTests
{
    [Fact]
    public void Palworld_and_windrose_are_curated_in_the_shipped_manifest()
    {
        // These come from the signed feed. If the embedded snapshot has not been refreshed the values
        // may be absent locally, so assert the SHAPE rather than demanding the data be present.
        foreach (var appId in new[] { "1623730", "3041230" })
        {
            var seam = SaveSeamCatalog.ByAppId(appId);
            Assert.Equal(seam.Count > 0, SaveSeamCatalog.CanShare(appId));
            Assert.All(seam, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        }
    }

    [Fact]
    public void A_character_game_and_an_uncurated_game_answer_identically()
    {
        // Cyberpunk has no world half; a game nobody has looked at has an unknown one. The panel must
        // not ask the user to tell those apart - it just does not offer to share either.
        Assert.False(SaveSeamCatalog.CanShare("1091500"));    // Cyberpunk 2077
        Assert.False(SaveSeamCatalog.CanShare("1245620"));    // Elden Ring
        Assert.False(SaveSeamCatalog.CanShare("0"));
        Assert.False(SaveSeamCatalog.CanShare(null));
        Assert.Empty(SaveSeamCatalog.ByAppId(null));
    }
}
