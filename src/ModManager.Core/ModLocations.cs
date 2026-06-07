namespace ModManager.Core;

/// <summary>
/// Pure candidate mod-folder generation per engine. When a game is added we scan the game
/// root for these relative paths; the first that exists becomes the game's real mod location,
/// so mods already installed (by the user or another manager) show up immediately.
///
/// Unreal is the tricky case: the load folder lives under the *project* subfolder
/// (e.g. R5/Content/Paks/~mods), which the preset default of "Content/Paks/~mods" misses.
/// We emit project-scoped candidates first, then the root-level fallback.
/// </summary>
public static class ModLocations
{
    private static string P(params string[] parts) => System.IO.Path.Combine(parts);

    /// <summary>
    /// Relative mod-folder candidates for an engine, most-specific first, de-duplicated.
    /// <paramref name="projectNames"/> only matters for Unreal (the Content/Paks parent folders).
    /// Unknown / custom engines return empty — we don't guess where mods live.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? engine, IEnumerable<string>? projectNames)
    {
        var result = new List<string>();
        void Add(string path) { if (!result.Contains(path)) result.Add(path); }

        switch (engine)
        {
            case "ue-pak":
                // De-facto UE load folders, under each project subfolder then at the root.
                string[] ueLeaves = { "~mods", "Mods", "LogicMods" };
                foreach (var proj in (projectNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)))
                    foreach (var leaf in ueLeaves)
                        Add(P(proj, "Content", "Paks", leaf));
                foreach (var leaf in ueLeaves)
                    Add(P("Content", "Paks", leaf)); // root-level fallback
                break;

            case "fromsoft":
                Add("mod"); // Mod Engine 2: each mod is a folder under 'mod'
                break;

            case "bethesda":
                Add("Data");
                break;

            case "bepinex":
                Add(P("BepInEx", "plugins"));
                break;

            case "smapi":
            case "melonloader":
                Add("Mods");
                break;

            case "minecraft":
                Add("mods");
                break;

            case "source":
                Add("addons");
                break;

            // "custom" and anything unrecognized: no guess.
        }

        return result;
    }

    /// <summary>The mod location for a single-project UE game: paks-root on Content/Paks for a
    /// loader-less game (mods sit directly in Paks), or the ~mods install target when a loader
    /// (UE4SS) mounts a ~mods subfolder. One source of truth for both the add-time seeder
    /// (EnginePresets) and the runtime resolver (ModLocator).</summary>
    public static ModLocation UePakModLocation(string project, bool loaderPresent)
        => loaderPresent
            ? new ModLocation("mods", "mods", System.IO.Path.Combine(project, "Content", "Paks", "~mods"))
            : new ModLocation("mods", "Paks", System.IO.Path.Combine(project, "Content", "Paks")) { Form = "paks-root" };
}
