namespace ModManager.Core.RestorePoints;

/// <summary>Pure decision: is any of a game's exe launch targets present in a set of running process
/// names? The App's GameProcessProbe supplies the running set from System.Diagnostics.Process; this
/// keeps the comparison logic headless-testable. Process names are extension-less, case-insensitive
/// (the Process.GetProcessesByName / Process.ProcessName convention).</summary>
public static class ProcessNameMatch
{
    /// <param name="installExeNames">Extension-less names of the executables in the game's install
    /// folder (top level), supplied by the App probe. The decisive coverage source for the common case:
    /// a steam-launched game has only a <c>steam://</c> target (no exe target) and usually isn't in the
    /// engine runtime map, yet it still runs a real .exe from its install dir during play. Matching those
    /// names catches it. Optional — omit for the pre-existing target+runtime-map behavior.</param>
    public static bool AnyRunning(GameEntry game, IReadOnlyCollection<string> runningProcessNames,
        IReadOnlyCollection<string>? installExeNames = null)
    {
        if (runningProcessNames.Count == 0) return false;
        var running = new HashSet<string>(runningProcessNames, StringComparer.OrdinalIgnoreCase);
        foreach (var t in game.LaunchTargets)
        {
            if (!string.Equals(t.Kind, "exe", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileNameWithoutExtension(t.Target);
            if (!string.IsNullOrEmpty(name) && running.Contains(name)) return true;
        }

        // Also match the engine's real runtime exe(s). A bootstrapper launch target (Seamless's
        // ersc_launcher.exe, the ModEngine2 launcher) exits after spawning the game, so the only
        // thing actually running is the runtime exe — which is never a LaunchTarget. Without this
        // the game-running pre-flight false-negatives and a destructive Safe Clear proceeds mid-game.
        foreach (var name in EngineRuntimeProcesses.For(game.Engine))
            if (running.Contains(name)) return true;

        // Finally, match any executable physically in the game's install folder. This is what covers
        // the common steam-launch case (no exe target, engine not in the runtime map) — the game still
        // runs a real .exe from its install dir. A destructive guard biases fail-SAFE: matching broadly
        // risks an occasional spurious refusal (annoying, recoverable) but never a false "not running"
        // (which would reset over a live game). Bounded to install-dir exes so it can't match unrelated
        // background apps.
        if (installExeNames is not null)
            foreach (var name in installExeNames)
                if (!string.IsNullOrEmpty(name) && running.Contains(name)) return true;

        return false;
    }
}
