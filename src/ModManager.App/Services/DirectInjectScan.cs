using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Lists the direct-inject mods in a FromSoft game's exe folder as read-only rows. FromSoft keeps
/// its exe + loose mods under a "Game" subfolder, so we look there. The recognition is pure
/// (<see cref="DirectInject"/>); only the folder listing lives here.
/// </summary>
public static class DirectInjectScan
{
    public static IReadOnlyList<Mod> List(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return Array.Empty<Mod>();

        return DirectInject.Detect(Names(Directory.GetFiles), Names(Directory.GetDirectories))
            .Select(d => new Mod
            {
                Name = d.Name,
                Base = d.Name,
                Class = d.Kind,                 // chip: GRAPHICS / CO-OP / UPSCALER / GAMEPLAY / DLL
                Location = "direct-inject",       // chip: this is a loose-file mod, not Mod Engine 2
                Enabled = true,                   // present == active for injected mods
                Description = "Detected: " + d.Evidence,
                Files = new List<string> { d.Evidence },
            })
            .ToList();

        IReadOnlyList<string> Names(Func<string, string[]> list)
        {
            try { return list(folder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
            catch { return Array.Empty<string>(); }
        }
    }

    /// <summary>FromSoft games keep the exe + mods under a "Game" subfolder; fall back to the root.</summary>
    public static string? PlayFolder(string? gameRoot)
    {
        if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return null;
        var game = Path.Combine(gameRoot, "Game");
        return Directory.Exists(game) ? game : gameRoot;
    }
}
