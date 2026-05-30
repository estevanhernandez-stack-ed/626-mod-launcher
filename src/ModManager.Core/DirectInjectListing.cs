using System.IO;

namespace ModManager.Core;

/// <summary>
/// Read-only direct-inject (FromSoft loose-file) listing. Lists enabled mods recognized in the
/// game's play folder alongside disabled ones held in the data dir. Extracted from the App's
/// DirectInjectService so the App and the headless agent-access MCP list direct-inject mods through
/// one path. Recognition + holding logic is pure/tested in <see cref="DirectInject"/>; this resolves
/// the folders and maps to <see cref="Mod"/>.
/// </summary>
public static class DirectInjectListing
{
    /// <summary>True for FromSoft games (the engine whose mods can be direct-inject).</summary>
    public static bool Applies(GameEntry game) => game.Engine == "fromsoft";

    public static IReadOnlyList<Mod> List(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        return Enabled(folder).Select(d => Row(d, enabled: true))
            .Concat(DirectInject.ListDisabled(Holding(game)).Select(d => Row(d, enabled: false)))
            .ToList();
    }

    // All currently-enabled direct-inject mods: top-level signatures PLUS the individual mods a DLL
    // loader runs from its mods\ folder. The bare "DLL mod loader" row is KEPT (tagged IsLoader in
    // Row) even when its mods\ folder has contents — it's load-bearing infrastructure the user must
    // be able to see and toggle, and its toggle cascades the whole stack (DirectInject.SetLoaderEnabled).
    public static IReadOnlyList<DirectInjectMod> Enabled(string? folder)
    {
        if (folder is null) return Array.Empty<DirectInjectMod>();
        var top = DirectInject.Detect(Names(folder, Directory.GetFiles), Names(folder, Directory.GetDirectories));

        var modsDir = Path.Combine(folder, "mods");
        var loaderMods = Directory.Exists(modsDir)
            ? DirectInject.DetectLoaderMods(Names(modsDir, Directory.GetFiles), Names(modsDir, Directory.GetDirectories))
            : Array.Empty<DirectInjectMod>();

        return top.Concat(loaderMods).ToList();
    }

    /// <summary>FromSoft games keep the exe + mods under a "Game" subfolder; fall back to the root.</summary>
    public static string? PlayFolder(string? gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return null;
        var game = Path.Combine(gameRoot, "Game");
        return Directory.Exists(game) ? game : gameRoot;
    }

    public static string Holding(GameEntry game) => Path.Combine(Scanner.DataDirForGame(game), "direct-disabled");

    private static Mod Row(DirectInjectMod d, bool enabled) => new()
    {
        Name = d.Name,
        Base = d.Name,
        Class = d.Kind,                 // chip: GRAPHICS / CO-OP / UPSCALER / DISPLAY / GAMEPLAY / DLL
        Location = "direct-inject",       // chip: loose-file mod, not Mod Engine 2
        Enabled = enabled,
        Description = "Detected: " + d.Evidence,
        Files = d.Entries.ToList(),
        // The bare DLL mod loader row: rendered distinguished (LOADER chip) and its toggle cascades.
        // DetectLoaderMods never emits LoaderName, so hosted-mod rows are never mis-tagged.
        IsLoader = d.Name == DirectInject.LoaderName,
    };

    private static IReadOnlyList<string> Names(string folder, Func<string, string[]> list)
    {
        try { return list(folder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
