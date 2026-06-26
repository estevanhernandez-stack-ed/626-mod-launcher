using System.IO;

namespace ModManager.Core.Loaders;

/// <summary>A catalog loader whose launcher exe was found in the play folder.</summary>
public sealed record DetectedLoader(KnownLoader Loader, string LauncherPath);

/// <summary>Pure detection: which KnownLoaders are installed in a game's play folder, and which
/// ban-safe loaders apply to a game. No I/O beyond File.Exists.</summary>
public static class LoaderScan
{
    private static bool Applies(KnownLoader l, string engine, string? steamAppId) =>
        string.Equals(l.Engine, engine, StringComparison.Ordinal)
        && (l.SteamAppId is null || string.Equals(l.SteamAppId, steamAppId, StringComparison.Ordinal));

    public static IReadOnlyList<DetectedLoader> Detect(string? playFolder, string engine, string? steamAppId)
    {
        if (string.IsNullOrWhiteSpace(playFolder) || !Directory.Exists(playFolder))
            return Array.Empty<DetectedLoader>();
        var found = new List<DetectedLoader>();
        foreach (var l in KnownLoaderCatalog.Catalog)
        {
            if (!Applies(l, engine, steamAppId)) continue;
            foreach (var exe in l.LauncherExeNames)
            {
                var p = Path.Combine(playFolder, exe);
                if (File.Exists(p)) { found.Add(new DetectedLoader(l, p)); break; }
            }
        }
        return found;
    }

    public static IReadOnlyList<KnownLoader> BanSafeFor(string engine, string? steamAppId) =>
        KnownLoaderCatalog.Catalog.Where(l => l.BanSafe && Applies(l, engine, steamAppId)).ToList();
}
