namespace ModManager.Core;

/// <summary>
/// Pure launch-enforcement verdicts. Some mods only work through their own launcher (Seamless
/// Co-op -> ersc_launcher.exe); launching vanilla with mods enabled is a silently-broken state.
/// When a game declares a <see cref="GameEntry.RequiredLauncher"/> and mods are enabled, that
/// launcher is the default Play and a vanilla launch confirms first (steer hard, keep the escape
/// hatch). Verdicts only — the App resolves the launcher path and shows the confirm.
/// </summary>
public static class LaunchGuard
{
    /// <summary>True when the game declares a required launcher and at least one mod is enabled —
    /// the required launcher should be the default Play target. Mods off -> no friction.</summary>
    public static bool RequiresLauncher(GameEntry game, bool anyModsEnabled)
        => anyModsEnabled && !string.IsNullOrEmpty(game.RequiredLauncher);

    /// <summary>True when launching <paramref name="target"/> should prompt a confirm first — i.e.
    /// enforcement is active and the target is a vanilla/steam launch (not an exe launcher).</summary>
    public static bool NeedsVanillaConfirm(GameEntry game, bool anyModsEnabled, LaunchTarget target)
        => RequiresLauncher(game, anyModsEnabled) && target.Kind != "exe";
}
