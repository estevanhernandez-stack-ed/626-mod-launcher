using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// The curated save-folder hint for a game — a facade over <see cref="EffectiveManifest"/>
/// (twin of <see cref="BanRiskCatalog"/> and <see cref="SaveLayoutCatalog"/>).
///
/// <para><b>This is what makes the hint load-bearing.</b> Until now nothing read
/// <c>saveDirHint</c> at all: detection went straight to Ludusavi's template list and took the first
/// path that existed on disk. That is usually right and sometimes precisely wrong — Stellaris resolves
/// to the game's CONFIG directory, because Ludusavi lists it first and it exists.</para>
///
/// <para>It matters more now than it did, because <see cref="SaveLayoutCatalog"/> describes the folder
/// the hint points at. A layout declared against one folder and applied to another would list
/// <c>.launcher-cache</c> and <c>logs</c> to a player as though they were saves.</para>
/// </summary>
public static class SaveDirHints
{
    /// <summary>The curated hint, still holding its <c>&lt;winDocuments&gt;</c>-style placeholders, or
    /// null when the feed says nothing.</summary>
    public static string? ByAppId(string? steamAppId)
        => Hint(ManifestIdLookup.EntryBySteamAppId(steamAppId));

    /// <summary>The curated hint for a registered game, resolved through every identity it carries
    /// (<see cref="ManifestIdLookup.EntryFor"/>). An EA app game has no Steam id at all, and a second
    /// store copy has an <c>&lt;id&gt;-2</c> id; both still name their manifest entry.</summary>
    public static string? For(GameEntry? game)
        => Hint(ManifestIdLookup.EntryFor(game));

    private static string? Hint(GameManifestEntry? entry)
        => string.IsNullOrWhiteSpace(entry?.SaveDirHint) ? null : entry!.SaveDirHint;
}
