using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// App-layer bridge for direct-inject FromSoft mods (loose files in the game's exe folder, no Mod
/// Engine 2). Lists enabled mods (recognized in the "Game" folder) alongside disabled ones (held
/// in the game's data dir), and toggles them via the reversible Core ops. The recognition + the
/// move logic are pure/tested in <see cref="DirectInject"/>; this resolves the folders.
/// </summary>
public sealed class DirectInjectService
{
    /// <summary>True for FromSoft games (the engine whose mods can be direct-inject).</summary>
    public bool Applies(GameEntry game) => game.Engine == "fromsoft";

    public IReadOnlyList<Mod> List(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        var enabled = folder is null
            ? Array.Empty<DirectInjectMod>()
            : DirectInject.Detect(Names(folder, Directory.GetFiles), Names(folder, Directory.GetDirectories));

        return enabled.Select(d => Row(d, enabled: true))
            .Concat(DirectInject.ListDisabled(Holding(game)).Select(d => Row(d, enabled: false)))
            .ToList();
    }

    /// <summary>Install dropped sources (zip/files/folders) into the game's exe folder.</summary>
    public IntakeResult Install(GameEntry game, IEnumerable<string> paths)
    {
        var folder = PlayFolder(game.GameRoot);
        return folder is null ? new IntakeResult() : DirectInject.Install(folder, paths);
    }

    public void SetEnabled(GameEntry game, string modName, bool enabled)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = Holding(game);
        if (enabled) { DirectInject.Enable(folder, holding, modName); return; }

        var mod = DirectInject.Detect(Names(folder, Directory.GetFiles), Names(folder, Directory.GetDirectories))
            .FirstOrDefault(m => m.Name == modName);
        if (mod is not null) DirectInject.Disable(folder, holding, mod);
    }

    private static Mod Row(DirectInjectMod d, bool enabled) => new()
    {
        Name = d.Name,
        Base = d.Name,
        Class = d.Kind,                 // chip: GRAPHICS / CO-OP / UPSCALER / DISPLAY / GAMEPLAY / DLL
        Location = "direct-inject",       // chip: loose-file mod, not Mod Engine 2
        Enabled = enabled,
        Description = "Detected: " + d.Evidence,
        Files = d.Entries.ToList(),
    };

    /// <summary>FromSoft games keep the exe + mods under a "Game" subfolder; fall back to the root.</summary>
    public static string? PlayFolder(string? gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return null;
        var game = Path.Combine(gameRoot, "Game");
        return Directory.Exists(game) ? game : gameRoot;
    }

    private static string Holding(GameEntry game) => Path.Combine(Scanner.DataDirForGame(game), "direct-disabled");

    private static IReadOnlyList<string> Names(string folder, Func<string, string[]> list)
    {
        try { return list(folder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
