namespace ModManager.Core.RestorePoints;

/// <summary>
/// Engine -> the real runtime process name(s) a game runs under, for the cases where the
/// configured launch target is a bootstrapper that exits after spawning the game. FromSoft's
/// Seamless Co-op (<c>ersc_launcher.exe</c>) and ModEngine2 launcher both patch + spawn the game
/// and then exit, so the only thing actually running is the engine runtime exe (<c>eldenring.exe</c>
/// behind the EAC bootstrapper <c>start_protected_game.exe</c>) — and that is never a LaunchTarget.
///
/// The game-running pre-flight matches these in ADDITION to the exe launch targets, so a destructive
/// Safe Clear refuses while the game is genuinely running, not only during the few seconds the
/// bootstrapper itself is alive. Without this, the refusal false-negatives for the exact engine class
/// the launcher targets most and a reset can run over a live game's files.
///
/// Names are stored extension-less, matching the <see cref="ProcessNameMatch"/> /
/// Process.ProcessName convention.
/// </summary>
public static class EngineRuntimeProcesses
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Map =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // FromSoft (Elden Ring + adjacents): the game runs as eldenring.exe behind the EAC
            // bootstrapper start_protected_game.exe; neither is ever a configured LaunchTarget.
            // Extend with darksouls3 / sekiro / armoredcore6 as those runtime names are verified.
            ["fromsoft"] = new[] { "eldenring", "start_protected_game" },
        };

    /// <summary>Extension-less runtime process names for an engine, or empty for null/unknown engines.</summary>
    public static IReadOnlyList<string> For(string? engine)
        => engine is not null && Map.TryGetValue(engine, out var names) ? names : Array.Empty<string>();
}
