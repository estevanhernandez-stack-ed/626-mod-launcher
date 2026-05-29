namespace ModManager.Core.RestorePoints;

/// <summary>Pure decision: is any of a game's exe launch targets present in a set of running process
/// names? The App's GameProcessProbe supplies the running set from System.Diagnostics.Process; this
/// keeps the comparison logic headless-testable. Process names are extension-less, case-insensitive
/// (the Process.GetProcessesByName / Process.ProcessName convention).</summary>
public static class ProcessNameMatch
{
    public static bool AnyRunning(GameEntry game, IReadOnlyCollection<string> runningProcessNames)
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

        return false;
    }
}
