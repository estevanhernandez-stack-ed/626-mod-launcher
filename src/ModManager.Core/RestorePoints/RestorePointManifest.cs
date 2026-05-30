namespace ModManager.Core.RestorePoints;

/// <summary>Version + sentinel constants for restore-point manifests.</summary>
public static class RestorePoint
{
    /// <summary>Bump when the manifest shape changes. Restore refuses any manifest whose
    /// schemaVersion exceeds the running build's supported value.</summary>
    public const int SchemaVersion = 1;
}

/// <summary>The sealed on-disk record of a Safe Clear. camelCase JSON. <c>Complete</c> is the seal,
/// written LAST by the orchestrator — Restore refuses a manifest that isn't complete.</summary>
public sealed record RestorePointManifest(
    int SchemaVersion,
    string LauncherVersion,
    string CreatedUtc,
    bool Complete,
    bool KeepNexus,
    long TotalBytes,
    int FileCount,
    IReadOnlyList<GameArchive> Games);

/// <summary>One game's archived state. EndState is "vanilla" | "modsActive".</summary>
public sealed record GameArchive(
    string Id,
    string GameName,
    string GameRoot,
    string EndState,
    IReadOnlyList<LaunchTarget> LaunchTargets,
    string? RequiredLauncher,
    IReadOnlyList<FrameworkArchive> Frameworks,
    IReadOnlyList<LoaderModState> LoaderMods,
    IReadOnlyList<OwnedModNote> OwnedMods,
    IReadOnlyList<MovedFile> MovedFiles,
    IReadOnlyList<ArchivedMod> Mods,
    string? OffboardingSheetGameFolderPath,
    // The game's live save folder (untouched by Safe Clear) + how many launcher-made save backups were
    // captured into this restore point. Surfaced on the off-boarding sheet so a reset never leaves the
    // user wondering about their saves. Defaulted/nullable — additive, no SchemaVersion bump.
    string? SaveLocation = null,
    int SaveBackupCount = 0);

/// <summary>A framework whose install state was captured before any uninstall. CapturedStateRel is
/// the archive-relative folder holding the captured installed files (with live config edits).</summary>
public sealed record FrameworkArchive(
    string FrameworkId,
    string DisplayName,
    string Author,
    string InstallPath,
    IReadOnlyList<string> InstalledFiles,
    string? CapturedStateRel);

/// <summary>A loader-driven mod (UE4SS/BepInEx) whose enable state lives in a manifest, not files.</summary>
public sealed record LoaderModState(string Name, string Loader, bool Enabled, string Location);

/// <summary>A mod managed by an external tool (Vortex/MO2) — noted, never moved by Safe Clear.</summary>
public sealed record OwnedModNote(string Name, string ManagedBy);

/// <summary>A game-folder file moved into the archive's vanilla-moved/ tree. Rel is relative to the
/// game root; Sha256 lets restore verify byte-for-byte.</summary>
public sealed record MovedFile(string Rel, long Bytes, string? Sha256);

/// <summary>A mod's provenance line for the off-boarding sheet.</summary>
public sealed record ArchivedMod(
    string Name,
    bool Enabled,
    string? SourceUrl,
    string? SourceConfidence,
    string? InstalledUtc);
