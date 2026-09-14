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

    /// <summary>True when a process-load proxy DLL physically sits at the TOP of the play folder — the
    /// OS auto-loads it into a plain vanilla/steam launch and crashes it, so the launcher steps aside
    /// (warns) first. Checks the filesystem directly, NOT the mod-row list: <see cref="Enabled"/> drops
    /// the loader row when its mods\ folder has contents, which would hide dinput8.dll from this check
    /// (the bug that let a vanilla launch crash silently). The hijack is a fact of disk, not of rows.</summary>
    public bool AnyActiveProxyDll(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return false;
        string[] topLevel;
        try { topLevel = Directory.GetFiles(folder); }
        catch { return false; }
        return DirectInject.AnyProcessLoadProxy(topLevel);
    }

    /// <summary>The process-load proxy DLL filenames currently sitting at the top of the play folder.
    ///
    /// <para>NOT gated on engine, deliberately. It was, on "fromsoft", and that is how a Monster
    /// Hunter Wilds install reported a successful vanilla launch while REFramework's dinput8.dll went
    /// on hijacking the process — a seventeen-month-old loader against a freshly patched build, so the
    /// game crashed on every start and the launcher said nothing. RE Engine, Unreal and Unity games
    /// use the same proxy filenames FromSoft games do; Windows loads none of them from a game folder
    /// unless something is hijacking, because the real ones live in System32. The comment on
    /// <see cref="DirectInject.AnyProcessLoadProxy"/> already said the hijack is a fact of the
    /// filesystem rather than of how the launcher displays rows — the engine gate contradicted it.</para></summary>
    public IReadOnlyList<string> ActiveProxyDlls(GameEntry game)
    {
        var folder = PlayFolder(game.GameRoot);
        if (folder is null) return Array.Empty<string>();
        string[] top;
        try { top = Directory.GetFiles(folder); } catch { return Array.Empty<string>(); }
        return DirectInject.ProcessLoadProxiesIn(top);
    }

    /// <summary>Step one active proxy DLL aside (reversible) for a vanilla launch. The move itself lives
    /// in <see cref="ModToggle"/>, shared with the agent-access MCP.</summary>
    public void DisableProxy(GameEntry game, string proxyDll) => ModToggle.SetProxyLoaderEnabled(game, proxyDll, enabled: false);

    /// <summary>Restore one proxy DLL stepped aside by <see cref="DisableProxy"/>.</summary>
    public void EnableProxy(GameEntry game, string proxyDll) => ModToggle.SetProxyLoaderEnabled(game, proxyDll, enabled: true);

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

    /// <summary>Toggle one direct-inject mod by name. Bulk callers (enable all, profiles) use this; the
    /// single-row toggle goes through <see cref="ModToggle.SetEnabledAsync"/>, which calls the same
    /// Core move.</summary>
    public void SetEnabled(GameEntry game, string modName, bool enabled) => ModToggle.SetDirectInjectEnabled(game, modName, enabled);

    /// <summary>FromSoft games keep the exe + mods under a "Game" subfolder; fall back to the root.</summary>
    public static string? PlayFolder(string? gameRoot) => DirectInjectListing.PlayFolder(gameRoot);
}
