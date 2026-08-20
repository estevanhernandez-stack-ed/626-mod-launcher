using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Save layout resolved from the signed manifest instead of a hardcoded app id.
///
/// <para>It used to be <c>steamAppId == "1623730" ? Worlds : TypedFiles</c>: one game recognised, and
/// a claim of <c>TypedFiles</c> asserted over 149 others nobody had checked — roughly 30 of which are
/// folder-per-save.</para>
/// </summary>
public class SaveLayoutCatalogTests
{
    [Fact]
    public void The_declared_value_is_read_and_anything_else_is_the_default()
    {
        Assert.Equal(SaveLayout.Worlds, SaveLayoutCatalog.Parse("worlds"));
        Assert.Equal(SaveLayout.Worlds, SaveLayoutCatalog.Parse("Worlds"));
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.Parse("typedFiles"));
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.Parse(null));
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.Parse(""));
    }

    [Fact]
    public void A_word_from_a_newer_feed_degrades_instead_of_throwing()
    {
        // The reason the manifest models this as a string rather than the enum. A throw here would be
        // caught by ManifestLoader as a JsonException and drop the ENTIRE feed - all 150 games - over
        // one unrecognised word.
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.Parse("perCharacterSlotsOrSomething"));
    }

    [Fact]
    public void Palworld_resolves_to_worlds_from_the_embedded_snapshot_with_no_feed_at_all()
    {
        // Offline, first launch, no cached feed. The embedded snapshot has to carry it or the panel
        // silently reverts to whole-folder behaviour for the one game this was all built on.
        Assert.Equal(SaveLayout.Worlds, SaveLayoutCatalog.ByAppId("1623730"));
        Assert.Equal(SaveLayout.Worlds, GameSaveTypesCatalog.Resolve("ue-pak", "1623730").Layout);
    }

    [Fact]
    public void A_game_the_manifest_says_nothing_about_gets_the_floor_not_a_third_state()
    {
        // Null in the manifest means "nobody looked", which matters to a curator. At runtime it must
        // collapse to what every game does today - whole-folder backup and restore - rather than
        // becoming a third state the UI has to explain.
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.ByAppId("1245620"));   // Elden Ring
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.ByAppId("0"));
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.ByAppId(null));
        Assert.Equal(SaveLayout.TypedFiles, SaveLayoutCatalog.ByAppId(""));
    }
}
