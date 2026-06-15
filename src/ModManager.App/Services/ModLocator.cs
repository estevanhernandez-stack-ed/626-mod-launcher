using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Finds where a freshly-added game's mods actually live by checking the engine's candidate
/// folders (<see cref="ModLocations.Candidates"/>) against the game root. Folders that already
/// exist become the game's mod locations, so mods installed earlier — by the user or another
/// manager (sideloaded) — show up the moment the game is added.
///
/// The pure candidate logic stays in Core and is unit-tested; only the existence check and the
/// Unreal project-folder discovery (both IO) live here.
/// </summary>
public static class ModLocator
{
    /// <summary>
    /// Mod locations for a game root, most-specific first. Existing candidate folders win.
    /// For Unreal with no mods yet but a single discoverable project, returns the canonical
    /// project path as the install target (the root-level preset default would miss it).
    /// Empty result means "keep the preset default" — we found nothing to correct.
    /// </summary>
    public static IReadOnlyList<ModLocation> Detect(string? gameRoot, string? engine)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return Array.Empty<ModLocation>();

        var candidates = engine == "ue-pak" ? UeProjectScan.Enumerate(gameRoot) : null;
        var projects = candidates?.Select(c => c.RelativeProjectPath).ToList();

        var existing = new List<ModLocation>();
        foreach (var rel in ModLocations.Candidates(engine, projects))
        {
            if (!Directory.Exists(Path.Combine(gameRoot, rel))) continue;
            existing.Add(new ModLocation(Name(existing.Count), rel, rel));
        }
        if (existing.Count > 0) return existing;

        // No loader folder matched. If the resolver picks exactly one project, seed from the disk fact:
        // Content/Paks present -> loader-less paks-root; not yet present -> the ~mods install target.
        // Ambiguous / none -> keep the preset default (don't guess), exactly as before.
        if (engine == "ue-pak" && candidates is { Count: > 0 })
        {
            var pick = UeProjectScan.Pick(candidates);
            if (pick.Kind == UeProjectPickKind.One && pick.Chosen is { } chosen)
            {
                var rel = chosen.RelativeProjectPath;
                var paksExists = Directory.Exists(Path.Combine(gameRoot, rel, "Content", "Paks"));
                return new[] { paksExists
                    ? ModLocations.UePakModLocation(rel, loaderPresent: false)
                    : ModLocations.UePakModLocation(rel, loaderPresent: true) };
            }
        }

        return Array.Empty<ModLocation>();
    }

    // Distinct location keys so per-location identity (disable meta, mirrors) stays unambiguous.
    private static string Name(int idx) => idx == 0 ? "mods" : "mods" + (idx + 1);
}
