using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID -> mod folder, straight off the manifest's per-game <c>modPath</c>.
///
/// <para>The engine preset knows where a TYPICAL game of that engine keeps its mods; it cannot know
/// where THIS one does. Unreal games prefix the project name (<c>Pal/Content/Paks/~mods</c>,
/// <c>Phoenix/Content/Paks/~mods</c>), RE Engine games use <c>reframework/autorun</c>, Cyberpunk uses
/// <c>archive/pc/mod</c> — none of which any preset can derive. <see cref="GameManifest.ModPath"/> is
/// documented as "override to the engine-default mod folder", and this is the facade that lets a
/// caller honour that instead of silently taking the preset.</para>
///
/// <para>Deliberately NOT provenance-filtered, unlike <see cref="KnownEngines"/>. That facade narrows
/// to one legacy array to keep its membership identical to what it always was; a mod path has no such
/// history. Any manifest entry that states one means it.</para>
/// </summary>
public static class KnownModPaths
{
    /// <summary>The curated mod folder for a Steam app id, or null when the manifest states none. Read
    /// through <see cref="ManifestIdLookup"/>, so the add path picks the same entry the scan joins to.</summary>
    public static string? ByAppId(string? steamAppId)
        => ManifestIdLookup.EntryBySteamAppId(steamAppId)?.ModPath is { } p && !string.IsNullOrWhiteSpace(p) ? p : null;
}
