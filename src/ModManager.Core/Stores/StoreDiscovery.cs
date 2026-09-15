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
        var games = manifestGames.ToList();

        return installed.Where(ig => ig.StoreKind == EaInstallScan.StoreKind
                ? !eaIds.Contains(ig.AppId) && EaGameImport.Match(ig, games) is not null
                : !steamIds.Contains(ig.AppId))
            .ToList();
    }
}
