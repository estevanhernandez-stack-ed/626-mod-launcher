using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// App-layer bridge for direct-inject FromSoft mods (loose files in the game's exe folder, no Mod
/// Engine 2). Handles the write + detection ops: toggle enable/disable, install/plan/execute drops,
/// Seamless Co-op detection, and proxy-DLL checks. Listing moved to <see cref="DirectInjectListing"/>
/// (shared with the agent-access MCP); the reversible move logic is pure/tested in <see cref="DirectInject"/>.
/// </summary>
public sealed class DirectInjectService
{
    /// <summary>True for FromSoft games (the engine whose mods can be direct-inject).</summary>
    public bool Applies(GameEntry game) => DirectInjectListing.Applies(game);

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

    // DLL proxies the OS auto-loads into ANY process started from the game folder (the loader-hijack
    // surface). An enabled direct-inject mod owning one of these crashes a plain vanilla/steam launch
    // ("The application was unable to start correctly").
    private static readonly string[] ProcessLoadProxies =
        { "dinput8.dll", "dxgi.dll", "d3d11.dll", "d3d9.dll", "version.dll", "winmm.dll", "winhttp.dll", "ersc.dll" };

    /// <summary>True when an enabled direct-inject mod owns a process-load proxy DLL — those load into a
    /// vanilla launch and crash it, so the launcher steps aside (warns) before a vanilla/steam launch.</summary>
    public bool AnyActiveProxyDll(GameEntry game)
    {
        if (game.Engine != "fromsoft") return false;
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return false;
        return Enabled(folder).Any(m => m.Entries.Any(e =>
            ProcessLoadProxies.Contains(Path.GetFileName(e), StringComparer.OrdinalIgnoreCase)));
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

    /// <summary>FromSoft games keep the exe + mods under a "Game" subfolder; fall back to the root.</summary>
    public static string? PlayFolder(string? gameRoot) => DirectInjectListing.PlayFolder(gameRoot);

    private static string Holding(GameEntry game) => DirectInjectListing.Holding(game);

    private static IReadOnlyList<DirectInjectMod> Enabled(string? folder) => DirectInjectListing.Enabled(folder);
}
