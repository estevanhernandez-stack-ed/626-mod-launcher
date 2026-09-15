using ModManager.Core.Manifest;

namespace ModManager.Core.Stores;

/// <summary>Which installed games the discovery lane offers: not already registered, keyed by store, and
/// for EA only the ones the manifest knows (see <see cref="EaGameImport"/>).</summary>
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
                : !steamIds.Contains(ig.AppId))
            .ToList();
    }

    /// <summary>Full path, trailing separator trimmed, case-insensitive compare — so "C:\Games\X" and
    /// "C:\Games\X\" (or a differently-cased drive letter) are the same folder for this check.</summary>
    private static string NormalizeDir(string? path)
        => string.IsNullOrEmpty(path)
            ? ""
            : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
