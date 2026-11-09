namespace ModManager.Core;

/// <summary>
/// Which launch target Play runs, by mode. Pure: the App supplies whether Seamless Co-op is fully installed
/// (a file probe) and runs whatever comes back.
/// <para>Modded prefers the Seamless launcher when Seamless is wired, else the game's default target. Vanilla
/// never returns a mod launcher: Seamless's <c>ersc_launcher.exe</c> and the ModEngine2 launcher both start the
/// game without anti-cheat and load their own mods, so a "vanilla" launch through either is neither vanilla nor
/// safe online. Found on the 2026-11-07 Patch Day smoke, where Play vanilla stepped the mods aside and then ran
/// <c>ersc_launcher.exe</c>.</para>
/// </summary>
public static class LaunchTargetPicker
{
    /// <summary>The target for <paramref name="mode"/>, or null. Null in modded mode means "no registered
    /// targets, use the legacy LaunchUrl / LaunchExe path". Null in vanilla mode means there is no way to start
    /// this game without a mod launcher, and the caller must not launch.</summary>
    public static LaunchTarget? Pick(GameEntry game, LaunchMode mode, bool seamlessFullyInstalled)
        => mode == LaunchMode.Vanilla ? PickVanilla(game) : PickModded(game, seamlessFullyInstalled);

    private static LaunchTarget? PickModded(GameEntry game, bool seamlessFullyInstalled)
    {
        if (seamlessFullyInstalled)
        {
            var seamless = game.LaunchTargets.FirstOrDefault(t =>
                string.Equals(t.Kind, "exe", StringComparison.OrdinalIgnoreCase)
                && (t.Target ?? "").Contains("ersc_launcher", StringComparison.OrdinalIgnoreCase));
            if (seamless is not null) return seamless;
        }
        return game.LaunchTargets.FirstOrDefault(t => t.IsDefault) ?? game.LaunchTargets.FirstOrDefault();
    }

    private static LaunchTarget? PickVanilla(GameEntry game)
    {
        var steam = game.LaunchTargets.FirstOrDefault(t => string.Equals(t.Kind, "steam", StringComparison.OrdinalIgnoreCase));
        if (steam is not null) return steam;

        if (!string.IsNullOrWhiteSpace(game.LaunchUrl))
            return new LaunchTarget("Play vanilla",
                game.LaunchUrl.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ? "steam" : "url",
                game.LaunchUrl);

        if (!string.IsNullOrWhiteSpace(game.SteamAppId))
            return new LaunchTarget("Play vanilla (Steam)", "steam", $"steam://rungameid/{game.SteamAppId}");

        var plain = game.LaunchTargets.FirstOrDefault(t => !IsModLauncher(t.Target) && !t.SkipsAntiCheat);
        if (plain is not null) return plain;

        if (!string.IsNullOrWhiteSpace(game.LaunchExe) && !IsModLauncher(game.LaunchExe))
            return new LaunchTarget("Play vanilla", "exe", game.LaunchExe);

        return null;
    }

    /// <summary>True for an executable that starts the game through a mod loader: Seamless Co-op's launchers
    /// (by name across versions, or any <c>*seamless*launch*.exe</c>, matching <see cref="LaunchScan.FindSeamless"/>)
    /// and the ModEngine2 launcher.</summary>
    public static bool IsModLauncher(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        // Reforged's launcher, or a .BAT that runs it: an overhaul, never a vanilla way in.
        if (ReforgedLaunch.IsReforgedLauncherPath(path)) return true;
        var name = path.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(name, "ersc_launcher.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "launch_elden_ring_seamlesscoop.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, ModEngine2.LauncherExe, StringComparison.OrdinalIgnoreCase)
            || (name.Contains("seamless", StringComparison.OrdinalIgnoreCase)
                && name.Contains("launch", StringComparison.OrdinalIgnoreCase));
    }
}
