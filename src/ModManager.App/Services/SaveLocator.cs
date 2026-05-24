using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Finds a game's save folder so the user never has to hunt for it. Builds candidate paths from
/// the real OS folders (Documents / LocalAppData / AppData) via Core's pure guesser — including
/// Unreal "project name" folders discovered under the game root (UE saves under the internal
/// project name, e.g. R5 for Windrose, not the store name) — and returns the first that exists.
/// </summary>
public static class SaveLocator
{
    public static string? Detect(string gameName, string? engine, string? gameRoot)
    {
        var roots = new SaveLocations.SaveRoots(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        foreach (var candidate in SaveLocations.Guess(roots, gameName, engine, ProjectNames(gameRoot)))
        {
            try { if (Directory.Exists(candidate)) return candidate; }
            catch { /* skip an unreadable candidate */ }
        }
        return null;
    }

    /// <summary>Folder-derived names: UE project subfolders (those with a Content dir) + the root name.</summary>
    private static IEnumerable<string> ProjectNames(string? gameRoot)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return names;
        try
        {
            foreach (var sub in Directory.GetDirectories(gameRoot))
                if (Directory.Exists(Path.Combine(sub, "Content")))
                    names.Add(Path.GetFileName(sub));
            names.Add(Path.GetFileName(gameRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }
        catch { /* unreadable game folder */ }
        return names;
    }
}
