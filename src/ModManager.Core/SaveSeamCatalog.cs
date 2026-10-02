using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// The curated world/character seam for a game — a facade over <see cref="EffectiveManifest"/>
/// (twin of <see cref="SaveLayoutCatalog"/> and <see cref="BanRiskCatalog"/>).
///
/// <para><b>Empty is the answer for most games, and it is not a failure.</b> A game may have no seam
/// because nobody has curated one yet, or because it has no world half at all — Cyberpunk and Elden
/// Ring are your character inside somebody else's world. Both cases resolve to empty here, and the
/// caller does the same thing with them: it does not offer to share a world.</para>
///
/// <para>The UI never asks the user to distinguish those two. A control that exists only to explain
/// why it cannot work is worse than no control.</para>
/// </summary>
public static class SaveSeamCatalog
{
    /// <summary>The curated seam, or empty when there is none to use.</summary>
    public static IReadOnlyList<string> ByAppId(string? steamAppId)
        => Seam(ManifestIdLookup.EntryBySteamAppId(steamAppId));

    /// <summary>The curated seam for a registered game, resolved through every identity it carries
    /// (<see cref="ManifestIdLookup.EntryFor"/>), so a game with no Steam id is not shut out.</summary>
    public static IReadOnlyList<string> For(GameEntry? game)
        => Seam(ManifestIdLookup.EntryFor(game));

    /// <summary>Whether a world from this game can be shared without its player. The one question the
    /// panel asks before deciding whether the control exists at all.</summary>
    public static bool CanShare(string? steamAppId) => ByAppId(steamAppId).Count > 0;

    /// <inheritdoc cref="CanShare(string?)"/>
    public static bool CanShareFor(GameEntry? game) => For(game).Count > 0;

    private static IReadOnlyList<string> Seam(GameManifestEntry? entry)
        => entry?.SavePlayerPaths is { Count: > 0 } paths ? paths : Array.Empty<string>();
}
