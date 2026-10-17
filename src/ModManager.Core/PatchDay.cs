namespace ModManager.Core;

/// <summary>
/// A loader's newest published release, from the signed feed (<c>latestRelease</c>). Keyed by
/// <see cref="Loader"/>, the loader's product name, matched to <see cref="StaleLoader.Name"/> ignoring
/// case. Descriptive only: a newer release does not prove it supports the new game build, so callers say
/// "has an update", never "fixed".
/// </summary>
public sealed record LoaderRelease(string Loader, string Version, DateTime PublishedUtc, string Url);

/// <summary>Where a game stands after a patch. One state per game; see the Patch Day design, section 1.</summary>
public enum PatchDayState
{
    /// <summary>Nothing to say: no patch, no version-locked loader, or the loader is already newer.</summary>
    Quiet,
    /// <summary>Steam patched the game and a version-locked loader predates it.</summary>
    Patched,
    /// <summary>The player chose vanilla and the mods are set aside.</summary>
    PlayingVanilla,
    /// <summary>The feed shows a release of the stale loader published after the loader's file.</summary>
    UpdateAvailable,
    /// <summary>The loader file on disk is newer than the game executable again.</summary>
    LoaderUpdated,
}

/// <param name="BaselineBuildId"><see cref="GameEntry.LastKnownSteamBuildId"/>: the build the mods were last checked against.</param>
/// <param name="LiveBuildId">The build Steam reports now; null or empty when the game has no Steam manifest.</param>
/// <param name="Record"><see cref="GameEntry.PatchDay"/>, null while no patch is in play.</param>
/// <param name="Loaders">The stale-loader check. While <paramref name="Mode"/> is Vanilla the loader's files
/// are set aside, so a live scan finds none; a caller that wants <see cref="PatchDayState.UpdateAvailable"/>
/// then must report the set-aside loaders' dates (from the vanilla stash) as stale entries.</param>
/// <param name="Releases">The feed's latest release per loader; empty when there is no feed data.</param>
/// <param name="Mode">What is true on disk: Vanilla while the stash holds the stepped-aside loaders.</param>
public sealed record PatchDayInput(
    string? BaselineBuildId, string? LiveBuildId, PatchDayRecord? Record, StaleLoaderReport Loaders,
    IReadOnlyList<LoaderRelease> Releases, LaunchMode Mode);

/// <param name="State">The one state to show.</param>
/// <param name="Hidden">The player snoozed the chip and nothing has changed since.</param>
/// <param name="Record">The stored record as given; null when the game is Quiet or has not been detected yet.</param>
/// <param name="Loader">The stale loader the chip names; null when none is stale.</param>
/// <param name="Release">The release behind <see cref="PatchDayState.UpdateAvailable"/>; otherwise null.</param>
/// <param name="GameExeUtc">When the game executable was last written, the chip's "updated on" date; null when no executable was found.</param>
/// <param name="NeedsDetect">The live build is a patch the record does not hold yet (none, or an older one):
/// run <see cref="PatchDay.Detect"/>, take the restore point if <see cref="PatchDay.NeedsRestorePoint"/>, and save.</param>
public sealed record PatchDayVerdict(
    PatchDayState State, bool Hidden, PatchDayRecord? Record, StaleLoader? Loader, LoaderRelease? Release,
    DateTime? GameExeUtc, bool NeedsDetect)
{
    /// <summary>What the player chose; None before they do. The chip shrinks to a quiet note for Modded.</summary>
    public PatchDayChoice Choice => Record?.Choice ?? PatchDayChoice.None;

    public static readonly PatchDayVerdict Quiet = new(PatchDayState.Quiet, false, null, null, null, null, false);
}

/// <summary>What Bring my mods back leaves behind: the new baseline, and no record.</summary>
public sealed record PatchDayBringBack(string BaselineBuildId, PatchDayRecord? Record);

/// <summary>
/// Patch Day: a game updated, its version-locked loader didn't, and the player keeps playing. Pure: the
/// caller reads the build ids, the loader report and the feed, and writes back whatever record comes out.
/// Nothing here moves a game between states on its own; every file change is a button the player presses.
/// </summary>
public static class PatchDay
{
    public static PatchDayVerdict Evaluate(PatchDayInput input)
    {
        var report = input.Loaders;
        var record = input.Record;
        var live = input.LiveBuildId;
        var stale = report.Loaders.Count > 0;
        DateTime? exeUtc = report.GameExePath is null ? null : report.GameExeUtc;

        // The trigger needs BOTH halves. Updated with no stale loader is the existing UPDATED banner's
        // case; Patch Day isn't involved.
        var patched = SteamBuildCheck.Evaluate(input.BaselineBuildId, live) == SteamBuildStatus.Updated && stale;

        if (record is null)
            return patched
                ? new PatchDayVerdict(PatchDayState.Patched, false, null, report.Loaders[0], null, exeUtc, NeedsDetect: true)
                : PatchDayVerdict.Quiet;

        var newBuild = !string.IsNullOrEmpty(live) && live != record.BuildId;
        var needsDetect = patched && newBuild;

        PatchDayState state;
        StaleLoader? loader = stale ? report.Loaders[0] : null;
        LoaderRelease? release = null;

        if (!stale && input.Mode == LaunchMode.Modded && report.GameExePath is not null)
        {
            // The loader file is newer than the exe again. Judged only with the mods in place and an
            // executable to compare against: set aside for vanilla, the loader's files are simply not
            // there, and an empty list then means "no information", not "the loader is newer".
            state = PatchDayState.LoaderUpdated;
        }
        else if (NewestReleaseAfterLoader(report, input.Releases) is { } hit)
        {
            // Beats PlayingVanilla: it is the actionable news, and its chip also offers Bring back.
            state = PatchDayState.UpdateAvailable;
            (loader, release) = hit;
        }
        else
        {
            state = record.Choice == PatchDayChoice.Vanilla && input.Mode == LaunchMode.Vanilla
                ? PatchDayState.PlayingVanilla
                : PatchDayState.Patched;
        }

        // Snooze lasts until something changes: another patch, a release, or the loader file. Of the
        // states only these two can be quiet about it; the other two ARE the change.
        var hidden = record.SnoozedUntilChange && !newBuild
            && state is PatchDayState.Patched or PatchDayState.PlayingVanilla;

        return new PatchDayVerdict(state, hidden, record, loader, release, exeUtc, needsDetect);
    }

    /// <summary>The stale loader with the newest feed release published after the loader's own file, if
    /// any. A release older than the file on disk is what the player already has.</summary>
    private static (StaleLoader Loader, LoaderRelease Release)? NewestReleaseAfterLoader(
        StaleLoaderReport report, IReadOnlyList<LoaderRelease> releases)
    {
        foreach (var loader in report.Loaders)
        {
            var newest = releases
                .Where(r => string.Equals(r.Loader, loader.Name, StringComparison.OrdinalIgnoreCase)
                            && r.PublishedUtc > loader.LoaderUtc)
                .OrderByDescending(r => r.PublishedUtc)
                .FirstOrDefault();
            if (newest is not null) return (loader, newest);
        }
        return null;
    }

    /// <summary>
    /// A patch was seen. No record starts one; a record on a different build moves to it (a second patch
    /// before the player acted). Idempotent on the same build, so a re-check never rewrites the record.
    ///
    /// <para>A second patch keeps the FIRST restore point, and its error: the snapshot that matters is the
    /// setup from before any patch, and a newer one would already hold a broken loader. It clears the
    /// snooze, because the build moved. A Modded choice reopens to None (it was an informed choice about
    /// the old build); Vanilla stays, because the mods are still set aside on disk and nothing returns on
    /// its own.</para>
    /// </summary>
    public static PatchDayRecord Detect(PatchDayRecord? record, string liveBuildId, DateTime now)
    {
        if (record is null) return new PatchDayRecord { BuildId = liveBuildId, DetectedUtc = now };
        if (record.BuildId == liveBuildId) return record;
        return record with
        {
            BuildId = liveBuildId,
            DetectedUtc = now,
            SnoozedUntilChange = false,
            Choice = record.Choice == PatchDayChoice.Vanilla ? PatchDayChoice.Vanilla : PatchDayChoice.None,
        };
    }

    /// <summary>
    /// Whether Play should stop and ask: the game is Patched, the player has not chosen yet, and there is a
    /// stored record for the choice to land on. Snooze does not count as a choice, so a snoozed game that is
    /// still Patched asks again; Play is the moment the answer matters, and the snooze only hides the chip.
    /// Once a choice is recorded this is false and Play launches whatever the choice left on disk.
    /// </summary>
    public static bool ShouldAskAtPlay(PatchDayVerdict? verdict)
        => verdict is { State: PatchDayState.Patched, Choice: PatchDayChoice.None, Record: not null };

    /// <summary>
    /// Whether Play on the library home must look at this game before launching it. A stored Vanilla or
    /// Modded choice settles it for free: the question is answered, so Play launches. No record (a patch
    /// may not have been seen yet) or a record with no choice needs a fresh verdict; the answer to "open to
    /// ask" is then <see cref="ShouldAskAtPlay"/> on that verdict, so a patch that has since gone quiet
    /// launches instead of dropping the player into the game view.
    /// </summary>
    public static bool NeedsPlayCheck(PatchDayRecord? record) => record is null or { Choice: PatchDayChoice.None };

    /// <summary>
    /// The Library home row's state line, from the persisted record and the cheap launch-mode file check
    /// only (no Steam scan, no loader scan per row). Null when there is nothing to say. A Vanilla choice
    /// with the mods back in place is quiet: the player already undid it.
    /// </summary>
    public static string? LibraryLine(PatchDayRecord? record, LaunchMode mode) => record switch
    {
        null => null,
        { Choice: PatchDayChoice.Vanilla } => mode == LaunchMode.Vanilla ? "Playing vanilla since the patch" : null,
        _ => "Updated since your last modded launch",
    };

    public static PatchDayRecord Choose(PatchDayRecord record, PatchDayChoice choice) => record with { Choice = choice };

    /// <summary>"Tell me when it's fixed": hide the chip until the state or the build changes.</summary>
    public static PatchDayRecord Snooze(PatchDayRecord record) => record with { SnoozedUntilChange = true };

    /// <summary>Whether detection should still try for a snapshot: none saved yet. After a failure a later
    /// patch retries; once one is saved it is never replaced.</summary>
    public static bool NeedsRestorePoint(PatchDayRecord record) => record.RestorePointId is null;

    public static PatchDayRecord WithRestorePoint(PatchDayRecord record, string restorePointId)
        => record with { RestorePointId = restorePointId, RestorePointError = null };

    /// <summary>Records why the snapshot could not be taken. The warning never depends on it. A saved
    /// snapshot is left alone: an error beside a good id would contradict itself.</summary>
    public static PatchDayRecord WithRestorePointError(PatchDayRecord record, string reason)
        => record.RestorePointId is null ? record with { RestorePointError = reason } : record;

    /// <summary>Bring my mods back, once the files are restored: the baseline moves to the build the record
    /// holds and the record clears, so the game returns to Quiet.</summary>
    public static PatchDayBringBack BringBack(PatchDayRecord record) => new(record.BuildId, null);
}
