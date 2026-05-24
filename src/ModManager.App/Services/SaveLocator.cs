using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Finds a game's save folder so the user never has to hunt for it. Builds candidate paths
/// from the real OS folders (Documents / LocalAppData / AppData) via Core's pure guesser and
/// returns the first that exists.
/// </summary>
public static class SaveLocator
{
    public static string? Detect(string gameName, string? engine)
    {
        var roots = new SaveLocations.SaveRoots(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

        foreach (var candidate in SaveLocations.Guess(roots, gameName, engine))
        {
            try { if (Directory.Exists(candidate)) return candidate; }
            catch { /* skip an unreadable candidate */ }
        }
        return null;
    }
}
