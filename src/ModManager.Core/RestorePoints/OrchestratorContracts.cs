using ModManager.Core;

namespace ModManager.Core.RestorePoints;

/// <summary>What the user chose in the Safe Clear dialog (Phase 1B-2 builds this).</summary>
public sealed record SafeClearOptions
{
    public bool CreateRestorePoint { get; init; } = true;          // archive on by default, skippable
    public bool KeepNexus { get; init; } = true;                    // keep nexus.json (Law D)
    public string DefaultEndState { get; init; } = "vanilla";       // applied to games without an override
    public IReadOnlyDictionary<string, string> PerGameEndState { get; init; }
        = new Dictionary<string, string>();                         // gameId -> "vanilla" | "modsActive"
}

/// <summary>Result of a Safe Clear (for the dialog to report).</summary>
public sealed record SafeClearResult(
    bool Ok, string? RefusedReason, string? RestorePointTimestamp,
    IReadOnlyList<string> PerGameSheetPaths, IReadOnlyList<string> Warnings);

/// <summary>A pre-flight blocker (free space, game running, offline drive).</summary>
public sealed record PreflightBlocker(string Kind, string Detail);

public sealed record RestorePointInfo(string Timestamp, IReadOnlyList<string> GameNames, long TotalBytes, bool Complete);

public sealed record RestoreResult(bool Ok, string? RefusedReason, IReadOnlyList<RestoreConflict> Conflicts, IReadOnlyList<string> Warnings);

public sealed record InterruptedClear(string Timestamp, bool Sealed);

/// <summary>App-side seam: the nexus.json keep/skip decision (DPAPI lives in the App impl).</summary>
public interface INexusGate { bool IsConnected { get; } void DeleteStoredKey(); }

/// <summary>App-side seam: is any of a game's launch-target processes running?</summary>
public interface IGameRunningProbe { bool AnyRunning(GameEntry game); }

/// <summary>App-side seam: the registered games + their live contexts (LauncherService in the App).</summary>
public interface IGameProvider
{
    IReadOnlyList<GameEntry> Games { get; }
    GameContext ContextFor(GameEntry game);
    void ReplaceRegistry(IReadOnlyList<GameEntry> games);   // restore upserts; reset clears
}
