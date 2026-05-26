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
        return Enabled(folder).Select(d => Row(d, enabled: true))
            .Concat(DirectInject.ListDisabled(Holding(game)).Select(d => Row(d, enabled: false)))
            .ToList();
    }

    // All currently-enabled direct-inject mods: top-level signatures PLUS the individual mods a DLL
    // loader runs from its mods\ folder. When those are present the bare "DLL mod loader" row is
    // dropped — it's represented by its contents.
    private static IReadOnlyList<DirectInjectMod> Enabled(string? folder)
    {
        if (folder is null) return Array.Empty<DirectInjectMod>();
        var top = DirectInject.Detect(Names(folder, Directory.GetFiles), Names(folder, Directory.GetDirectories));

        var modsDir = Path.Combine(folder, "mods");
        var loaderMods = Directory.Exists(modsDir)
            ? DirectInject.DetectLoaderMods(Names(modsDir, Directory.GetFiles), Names(modsDir, Directory.GetDirectories))
            : Array.Empty<DirectInjectMod>();

        if (loaderMods.Count > 0) top = top.Where(m => m.Name != DirectInject.LoaderName).ToList();
        return top.Concat(loaderMods).ToList();
    }

    /// <summary>
    /// True when Seamless Co-op's mod files are present but its launcher is missing — co-op only
    /// starts through that launcher, so the bare DLL alone won't work. Drives the "needs launcher" flag.
    /// </summary>
    public bool SeamlessNeedsLauncher(GameEntry game)
        => IsSeamlessDllPresent(game) && LaunchScan.FindSeamless(game.GameRoot) is null;

    /// <summary>
    /// True when Seamless Co-op is fully wired (mod files + launcher exe both present). When true,
    /// the user does NOT need to flip vanilla anti-cheat off for modded Elden Ring — Seamless brings
    /// its own bypass and runs its own private multiplayer. Suppresses the "Launch options" warning.
    /// </summary>
    public bool SeamlessFullyInstalled(GameEntry game)
        => IsSeamlessDllPresent(game) && LaunchScan.FindSeamless(game.GameRoot) is not null;

    private bool IsSeamlessDllPresent(GameEntry game)
    {
        if (game.Engine != "fromsoft") return false;
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return false;
        return Directory.Exists(Path.Combine(folder, "SeamlessCoop"))
            || File.Exists(Path.Combine(folder, "ersc.dll"))
            || File.Exists(Path.Combine(folder, "SeamlessCoop", "ersc.dll"));
    }

    /// <summary>Install dropped sources (zip/files/folders) into the game's exe folder.</summary>
    public IntakeResult Install(GameEntry game, IEnumerable<string> paths)
    {
        var folder = PlayFolder(game.GameRoot);
        return folder is null ? new IntakeResult() : DirectInject.Install(folder, paths);
    }

    /// <summary>Plan a drop without touching disk — what's new, what collides, what's refused.</summary>
    public IntakePlan Plan(GameEntry game, IEnumerable<string> paths)
    {
        var folder = PlayFolder(game.GameRoot);
        return folder is null
            ? new IntakePlan(Array.Empty<IntakeItem>(), Array.Empty<IntakeCollision>(), Array.Empty<SkippedItem>())
            : DirectInject.Plan(folder, paths);
    }

    /// <summary>Execute a planned drop. Replaced originals are kept under the play folder's _626
    /// folder so they travel with the game and can be reverted.</summary>
    public IntakeResult Execute(GameEntry game, IntakePlan plan, ISet<string> replace)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return new IntakeResult();
        var replacedRoot = Path.Combine(folder, "_626", "replaced");
        return DirectInject.Execute(folder, replacedRoot, plan, replace);
    }

    public void SetEnabled(GameEntry game, string modName, bool enabled)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return;
        var holding = Holding(game);
        if (enabled) { DirectInject.Enable(folder, holding, modName); return; }

        var mod = Enabled(folder).FirstOrDefault(m => m.Name == modName);
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
