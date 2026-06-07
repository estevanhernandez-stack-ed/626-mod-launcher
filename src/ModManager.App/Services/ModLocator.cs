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

        var projects = engine == "ue-pak" ? UnrealProjects(gameRoot) : null;

        var existing = new List<ModLocation>();
        foreach (var rel in ModLocations.Candidates(engine, projects))
        {
            if (!Directory.Exists(Path.Combine(gameRoot, rel))) continue;
            existing.Add(new ModLocation(Name(existing.Count), rel, rel));
        }
        if (existing.Count > 0) return existing;

        // No loader folder matched above, but one clear UE project: decide the layout from the disk
        // fact. The pure decision lives in Core (UePakModLocation) so it stays unit-tested; we supply
        // the fact. Loader-less (Content/Paks exists) -> paks-root on Content/Paks (base-game-filtering
        // form). No Paks dir yet -> seed the ~mods install target.
        if (engine == "ue-pak" && projects is { Count: 1 })
        {
            var paksExists = Directory.Exists(Path.Combine(gameRoot, projects[0], "Content", "Paks"));
            return new[] { paksExists
                ? ModLocations.UePakModLocation(projects[0], loaderPresent: false)   // loader-less: paks-root
                : ModLocations.UePakModLocation(projects[0], loaderPresent: true) }; // no Paks yet: ~mods install target
        }

        return Array.Empty<ModLocation>();
    }

    // Distinct location keys so per-location identity (disable meta, mirrors) stays unambiguous.
    private static string Name(int idx) => idx == 0 ? "mods" : "mods" + (idx + 1);

    // UE projects are top-level subfolders that contain a Content directory (e.g. R5/Content).
    private static IReadOnlyList<string> UnrealProjects(string gameRoot)
    {
        try
        {
            return Directory.GetDirectories(gameRoot)
                .Where(d => Directory.Exists(Path.Combine(d, "Content")))
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }
}
