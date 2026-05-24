namespace ModManager.Core;

/// <summary>
/// Pure save-folder guessing. Combines the OS folder roots with engine/game patterns into a
/// ranked candidate list; the App checks which one exists. The point: a non-technical user
/// never has to hunt for their save folder. Mirrors the engine-detect split (Core decides
/// the candidates, App does the IO).
/// </summary>
public static class SaveLocations
{
    public sealed record SaveRoots(string Documents, string LocalAppData, string AppData);

    /// <param name="extraNames">
    /// Extra project-name candidates beyond the display name — e.g. an Unreal game's internal
    /// project folder ("R5" for Windrose), since UE saves under the project name, not the store name.
    /// </param>
    public static IReadOnlyList<string> Guess(SaveRoots roots, string gameName, string? engine, IEnumerable<string>? extraNames = null)
    {
        var names = new List<string> { (gameName ?? "").Trim() };
        if (extraNames is not null) names.AddRange(extraNames.Select(n => (n ?? "").Trim()));
        names = names.Where(n => n.Length > 0).Distinct().ToList();

        var unknown = string.IsNullOrEmpty(engine);
        var c = new List<string>();
        void Add(params string[] parts) => c.Add(System.IO.Path.Combine(parts));

        foreach (var name in names)
        {
            if (engine == "bethesda" || unknown)
            {
                Add(roots.Documents, "My Games", name, "Saves");
                Add(roots.Documents, "My Games", name);
            }
            if (engine == "ue-pak" || unknown)
            {
                Add(roots.LocalAppData, name, "Saved", "SaveGames");
                Add(roots.LocalAppData, name, "Saved", "SaveProfiles");
                Add(roots.LocalAppData, name, "Saved");
            }
            if (engine == "smapi" || unknown)
            {
                Add(roots.AppData, "StardewValley", "Saves");
            }

            // Generic fallbacks that catch most other games.
            Add(roots.Documents, "My Games", name);
            Add(roots.Documents, name);
            Add(roots.AppData, name);
            Add(roots.LocalAppData, name);
        }

        return c.Distinct().ToList();
    }
}
