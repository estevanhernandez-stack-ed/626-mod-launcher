namespace ModManager.Core.RestorePoints;

/// <summary>Fully-hydrated input to <see cref="OffBoardingSheet.Render"/>. The App builds this from
/// GameEntry.LaunchTargets + LaunchScan + DirectInject.Detect + FrameworkRegistry + metadata. The
/// renderer touches NO filesystem and carries NO Nexus account/key — only mod source URLs.</summary>
public sealed record OffBoardingReport(
    string GameName,
    string RestorePointPath,
    IReadOnlyList<string> LaunchLines,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<OffBoardingModLine> Mods,
    IReadOnlyList<OffBoardingOwnedMod> OwnedMods,
    // Saves are the user's irreplaceable data. Safe Clear NEVER touches the game's live save folder;
    // the sheet says so explicitly and names where it is, so a reset never leaves the user wondering.
    // SaveLocation is the live save path (null if the launcher had none recorded); SaveBackupCount is
    // how many launcher-made save backups were preserved into this restore point.
    string? SaveLocation = null,
    int SaveBackupCount = 0,
    // Vanilla with a turn-off record: how many mods were turned off and held, where they are held, and
    // the ones whose turn-off refused (still active). Zero / null renders no section (an older archive).
    int TurnedOffCount = 0,
    string? HeldInDataDir = null,
    IReadOnlyList<ClearSkip>? TurnOffSkips = null,
    // Where the turned-off mods went, per lane: held in the data folder (scanner, direct-inject, loose-root),
    // stepped aside into the game's _626\vanilla-proxy (proxy loaders), or switched off in Mod Engine 2's config.
    int InDataFolder = 0,
    int Proxies = 0,
    int InConfig = 0,
    // How many of the data-folder ones have a copy in the restore point (heldCopies). Only those may be
    // described as saved there; the rest are only in the data folder.
    int CopiedToRestorePoint = 0,
    // Vanilla with a turn-off record: the mods are kept and turned off, not "still installed" and live, so
    // the mod list is headed accordingly.
    bool KeptTurnedOff = false,
    // How many leftover files in the mod-only folders (no mod row claims them) went into the restore point,
    // and what vanilla knowingly left in place and why. Null = an older archive with no sweep.
    int RemainderMoved = 0,
    IReadOnlyList<InPlaceNote>? StillInPlace = null);

public sealed record OffBoardingOwnedMod(string Name, string ManagedBy);

public sealed record OffBoardingModLine(
    string Name,
    string? SourceUrl,
    string? SourceConfidence,   // "manual" | "fingerprint" | "md5" | "nameSearch" | null
    string? InstalledDate,      // pre-formatted yyyy-MM-dd or null
    // Vanilla with a turn-off record: where the mod stands after the reset. TurnedOff | StillActive |
    // AlreadyOff. Null on any other sheet (one list, as before).
    string? State = null,
    // Why a turned-off mod went off when it wasn't the toggle ("its loader was turned off"). Null otherwise.
    string? StateNote = null);

/// <summary>The states a mod line can carry on a vanilla sheet.</summary>
public static class OffBoardingModState
{
    public const string TurnedOff = "turned-off";
    public const string StillActive = "still-active";
    public const string AlreadyOff = "already-off";
    /// <summary>Never touched because of where it is: another tool's folder, a system folder, a location
    /// with no folder. Not "still active" by 626's choice, and never "turned off".</summary>
    public const string LeftAlone = "left-alone";
}
