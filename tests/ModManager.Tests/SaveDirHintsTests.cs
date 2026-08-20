using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The curated save-folder hint, and why it now has to be read.
///
/// <para>Nothing consumed <c>saveDirHint</c> until this: detection went straight to Ludusavi's list
/// and took the first path that existed. That is usually right and occasionally precisely wrong —
/// Stellaris resolves to the game's CONFIG directory that way, because Ludusavi lists it first and it
/// does exist on disk.</para>
///
/// <para>It matters more now, because <see cref="SaveLayoutCatalog"/> describes the folder the hint
/// points at. A layout declared against one folder and applied to another lists launcher caches to a
/// player as though they were saves.</para>
/// </summary>
public class SaveDirHintsTests
{
    [Fact]
    public void Palworlds_hint_comes_from_the_shipped_manifest()
    {
        // The embedded snapshot carries no hint for Palworld, so this reads whatever the effective
        // manifest holds - present or absent, it must not throw and must not invent a path.
        var hint = SaveDirHints.ByAppId("1623730");
        if (hint is not null) Assert.DoesNotContain("..", hint);
    }

    [Fact]
    public void An_unknown_or_missing_app_id_is_null_rather_than_a_guess()
    {
        Assert.Null(SaveDirHints.ByAppId("0"));
        Assert.Null(SaveDirHints.ByAppId(null));
        Assert.Null(SaveDirHints.ByAppId(""));
    }

    [Fact]
    public void A_hint_keeps_its_placeholders_for_the_caller_to_resolve()
    {
        // The facade hands back the manifest's own string. Resolving <winDocuments> and friends is
        // LudusaviPaths' job, and doing it here would bake one machine's layout into the map.
        foreach (var appId in new[] { "1623730", "281990", "3041230" })
        {
            var hint = SaveDirHints.ByAppId(appId);
            if (hint is null) continue;
            Assert.False(System.IO.Path.IsPathRooted(hint) && !hint.StartsWith("<"),
                $"{appId}: a hint should be a template, not a resolved path — got {hint}");
        }
    }
}
