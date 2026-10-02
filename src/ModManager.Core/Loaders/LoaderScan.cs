using System.IO;

namespace ModManager.Core.Loaders;

/// <summary>A catalog loader whose launcher exe was found in the play folder.</summary>
public sealed record DetectedLoader(KnownLoader Loader, string LauncherPath);

/// <summary>Pure detection: which KnownLoaders are installed in a game's play folder, and which
/// ban-safe loaders apply to a game. No I/O beyond File.Exists.</summary>
public static class LoaderScan
{
    /// <summary>
    /// Whether a loader applies to this registration. The engine must match; then an unpinned loader
    /// applies to every game on it, and a pinned one applies when the game's Steam app id OR its
    /// manifest id is among the pins.
    ///
    /// <para>Takes the whole game, never <c>(engine, steamAppId)</c>. Steam id alone was the shape of
    /// the ban-risk bug fixed for EA app games: they are registered with no Steam id, so every lookup
    /// keyed on one read them as nothing. The manifest id is what every registration carries.</para>
    /// </summary>
    private static bool Applies(KnownLoader l, GameEntry game)
    {
        if (string.IsNullOrEmpty(game.Engine) || !string.Equals(l.Engine, game.Engine, StringComparison.Ordinal))
            return false;
        if (!l.IsPinned) return true;
        if (l.SteamAppId is not null && string.Equals(l.SteamAppId, game.SteamAppId, StringComparison.Ordinal))
            return true;
        return l.GameIds is { } ids && !string.IsNullOrEmpty(game.Id) && ids.Contains(game.Id, StringComparer.Ordinal);
    }

    public static IReadOnlyList<DetectedLoader> Detect(string? playFolder, GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(playFolder) || !Directory.Exists(playFolder))
            return Array.Empty<DetectedLoader>();
        var found = new List<DetectedLoader>();
        foreach (var l in KnownLoaderCatalog.Catalog)
        {
            if (!Applies(l, game)) continue;
            foreach (var exe in l.LauncherExeNames)
            {
                var p = Path.Combine(playFolder, exe);
                if (File.Exists(p)) { found.Add(new DetectedLoader(l, p)); break; }
            }
        }
        return found;
    }

    public static IReadOnlyList<KnownLoader> BanSafeFor(GameEntry game) =>
        KnownLoaderCatalog.Catalog.Where(l => l.BanSafe && Applies(l, game)).ToList();
}
