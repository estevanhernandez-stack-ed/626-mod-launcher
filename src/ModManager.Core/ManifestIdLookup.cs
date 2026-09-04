using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Naming which curated game a store identifier refers to.
///
/// <para>A registered game joins to its manifest entry by ID (<c>Scanner.GameContext</c>), and that id
/// used to come from slugifying whatever display name was in the wizard's box. When the two disagreed
/// — "Minecraft: Java Edition" against the <c>minecraft</c> entry — every curated fact about the game
/// was silently discarded, with nothing reported. This lets the add path state which game it is instead
/// of inferring it from a name somebody typed.</para>
///
/// <para>Returning null is a normal answer, not a failure: a game outside the manifest, a machine whose
/// feed never loaded, a game with no Steam id. The caller falls back to the name-derived id, which is
/// exactly today's behaviour.</para>
/// </summary>
public static class ManifestIdLookup
{
    public static string? BySteamAppId(GameManifest? manifest, string? steamAppId)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(steamAppId)) return null;
        return manifest.Games
            .FirstOrDefault(g => string.Equals(g.Stores.SteamAppId, steamAppId, StringComparison.Ordinal))
            ?.Id;
    }
}
