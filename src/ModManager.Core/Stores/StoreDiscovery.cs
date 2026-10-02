using ModManager.Core.Manifest;

namespace ModManager.Core.Stores;

/// <summary>Which installed games the library lists as not managed (B6; once the discovery lane): not
/// already registered, by store id or by folder, keyed by store; for EA only the ones the manifest knows
/// (see <see cref="EaGameImport"/>); for Steam never a runtime or redistributable
/// (<see cref="SteamNonGames"/>).</summary>
public static class StoreDiscovery
{
    public static IReadOnlyList<InstalledGame> Offerable(
        IEnumerable<InstalledGame> installed, IEnumerable<GameEntry> registered, IEnumerable<GameManifestEntry> manifestGames)
    {
        var reg = registered.ToList();
        var steamIds = reg.Where(g => !string.IsNullOrEmpty(g.SteamAppId)).Select(g => g.SteamAppId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var eaIds = reg.Where(g => !string.IsNullOrEmpty(g.EaContentId)).Select(g => g.EaContentId!)
            .ToHashSet(StringComparer.Ordinal);
        // F4: eaIds alone misses a registration with no EaContentId recorded (a redetect, a manual
        // re-add, a hand-edited registry). The folder and the manifest id are two more facts that
        // already say "this install IS this registered game" — either one closes the gap, and closing
        // it also closes F5 (the import can never collide into a "-2" suffixed id and lose ban risk).
        var registeredIds = reg.Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registeredRoots = reg.Select(g => NormalizeDir(g.GameRoot)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var games = manifestGames.ToList();

        return installed.Where(ig => ig.StoreKind == EaInstallScan.StoreKind
                ? !eaIds.Contains(ig.AppId)
                    && !registeredRoots.Contains(NormalizeDir(ig.InstallDir))
                    && EaGameImport.Match(ig, games) is { } matched
                    && !registeredIds.Contains(matched.Id)
                // A Steam game registered without its app id (a + Game add with the box left blank) is
                // still that game: the folder says so, the same fact the EA branch uses. And a runtime or
                // redistributable is never a game to offer (B6 review: they became library rows).
                : !steamIds.Contains(ig.AppId)
                    && !registeredRoots.Contains(NormalizeDir(ig.InstallDir))
                    && !SteamNonGames.Is(ig))
            .ToList();
    }

    /// <summary>Full path, trailing separator trimmed, case-insensitive compare — so "C:\Games\X" and
    /// "C:\Games\X\" (or a differently-cased drive letter) are the same folder for this check.</summary>
    private static string NormalizeDir(string? path)
        => string.IsNullOrEmpty(path)
            ? ""
            : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
