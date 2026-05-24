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
    /// <summary>
    /// Authoritative-first: resolve the Ludusavi save templates for the Steam app id, then fall
    /// back to the folder heuristics. Best-effort — anything missing degrades to the heuristic.
    /// </summary>
    public static async Task<string?> DetectAsync(LudusaviService ludusavi, string gameName, string? engine, string? gameRoot, string? steamAppId)
    {
        if (!string.IsNullOrEmpty(steamAppId))
        {
            var tokens = WindowsTokens(gameRoot);
            foreach (var template in await ludusavi.SaveTemplatesAsync(steamAppId))
            {
                var resolved = LudusaviPaths.Resolve(template, tokens);
                if (resolved is null) continue;
                try { if (Directory.Exists(resolved)) return resolved; } catch { /* skip */ }
            }
        }
        return Detect(gameName, engine, gameRoot);
    }

    private static Dictionary<string, string> WindowsTokens(string? gameRoot) => new()
    {
        ["base"] = gameRoot ?? "",
        ["home"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ["winLocalAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ["winAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ["winDocuments"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ["winProgramData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        ["winPublic"] = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public",
        ["winDir"] = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows",
        ["osUserName"] = Environment.UserName,
    };

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
