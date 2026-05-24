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

    public static IReadOnlyList<string> Guess(SaveRoots roots, string gameName, string? engine)
    {
        var name = (gameName ?? "").Trim();
        var c = new List<string>();
        void Add(params string[] parts) => c.Add(System.IO.Path.Combine(parts));

        switch (engine)
        {
            case "bethesda":
                Add(roots.Documents, "My Games", name, "Saves");
                Add(roots.Documents, "My Games", name);
                break;
            case "ue-pak":
                Add(roots.LocalAppData, name, "Saved", "SaveGames");
                Add(roots.LocalAppData, name, "Saved");
                break;
            case "smapi":
                Add(roots.AppData, "StardewValley", "Saves");
                break;
        }

        // Generic fallbacks that catch most other games.
        Add(roots.Documents, "My Games", name);
        Add(roots.Documents, name);
        Add(roots.AppData, name);
        Add(roots.LocalAppData, name);

        return c.Distinct().ToList();
    }
}
