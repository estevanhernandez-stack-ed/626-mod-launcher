namespace ModManager.Core.Catalog;

/// <summary>
/// Resolve the absolute on-disk paths of a direct-inject mod's known config files. For each
/// <see cref="KnownDirectInjectMod.ConfigPaths"/> entry, an override (if set) wins; else the
/// path is computed relative to the mod's resolved install root (PlayFolder for FromSoft —
/// i.e., <c>&lt;gameRoot&gt;/Game/</c> when that subfolder exists, else <c>gameRoot</c>).
/// Only paths that ACTUALLY exist on disk are returned — a catalog entry without an installed
/// copy returns empty so the row's pencil icon stays hidden.
/// </summary>
public static class DirectInjectModConfigResolver
{
    public static IReadOnlyList<string> Resolve(
        string modDisplayName, string gameRoot, DirectInjectConfigOverrides overrides)
    {
        var entry = KnownDirectInjectMod.Catalog.FirstOrDefault(m => m.DisplayName == modDisplayName);
        if (entry is null) return Array.Empty<string>();

        var installRoot = ResolveInstallRoot(entry.InstallRoot, gameRoot);
        overrides.OverridesByModId.TryGetValue(entry.ModId, out var modOverrides);

        var resolved = new List<string>();
        foreach (var rel in entry.ConfigPaths)
        {
            string abs;
            if (modOverrides is not null && modOverrides.TryGetValue(rel, out var custom))
                abs = custom;
            else
            {
                // ConfigPaths use forward slashes (cross-platform-friendly catalog convention),
                // but downstream consumers (file-open, INI-editor) expect platform-native paths.
                // Normalize via GetFullPath so the returned strings are comparable on Windows.
                var relNative = rel.Replace('/', Path.DirectorySeparatorChar);
                abs = Path.GetFullPath(Path.Combine(installRoot, relNative));
            }

            if (File.Exists(abs)) resolved.Add(abs);
        }
        return resolved;
    }

    private static string ResolveInstallRoot(string installRootSymbol, string gameRoot)
    {
        return installRootSymbol switch
        {
            "GameRoot" => gameRoot,
            "PlayFolder" => ResolvePlayFolder(gameRoot),
            _ => gameRoot,
        };
    }

    private static string ResolvePlayFolder(string gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot)) return gameRoot;
        var game = Path.Combine(gameRoot, "Game");
        return Directory.Exists(game) ? game : gameRoot;
    }
}
