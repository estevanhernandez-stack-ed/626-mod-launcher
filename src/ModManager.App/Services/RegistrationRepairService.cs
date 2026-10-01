using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.App.Services;

/// <summary>The outcome of a registration save, in words the status bar can show verbatim.</summary>
public sealed record RepairSaveOutcome(bool Saved, string Message);

/// <summary>
/// Owns the registration-repair flow: read the shape, preview an edit's consequences, and save.
///
/// <para>Deliberately NOT in MainViewModel, which has 14 concrete service dependencies and cannot be
/// constructed in a test. Three times in recent work a decision parked there accumulated defects
/// until it was extracted to Core. This type is orchestration only — every decision it acts on is
/// computed in Core behind a test.</para>
/// </summary>
public sealed class RegistrationRepairService
{
    private readonly LauncherService _svc;

    public RegistrationRepairService(LauncherService svc) => _svc = svc;

    public GameShape Shape(GameEntry game) => GameShape.Of(game);

    /// <param name="walks">The calling dialog's own walk cache, so its keystroke previews walk the data
    /// dir once. Never held here: this service is a singleton and would outlive the folder it
    /// describes. <see cref="SaveAsync"/> deliberately plans without one.</param>
    public RegistrationChangePlan Preview(GameEntry stored, GameEntry proposed, DataDirWalkCache? walks = null)
        => RegistrationChange.Plan(stored, proposed, walks);

    /// <summary>
    /// Apply an edit.
    ///
    /// <para>ORDER IS THE SAFETY. The data-dir move runs BEFORE the registry write, so a failed move
    /// leaves nothing written anywhere — registration untouched, data untouched. A failed write AFTER
    /// a successful move would orphan the user's only copy of their disabled mods, so that case
    /// re-plans the move in reverse and runs it. If the reverse also fails, both absolute paths go
    /// into the message: silence is the only unacceptable outcome.</para>
    ///
    /// <para>IT DOES NOT RAISE <c>RegistryChanged</c>, deliberately. The event's only subscriber
    /// enqueues a full mod reload, and the caller already reloads explicitly after this returns — two
    /// concurrent rebuilds, of which the enqueued one finishes last and ends in a DIRECT StatusText
    /// assignment that erases the answer to the riskiest operation in the app. The caller's reload is
    /// also the one that rebuilds the games dropdown, which a rename needs.</para>
    /// </summary>
    public async Task<RepairSaveOutcome> SaveAsync(
        GameEntry stored, GameEntry proposed, bool moveDataDir, IProgress<(int Copied, int Total)>? progress)
    {
        // A FRESH plan, never a preview's cached walk: this is the plan the move is executed from.
        var plan = Preview(stored, proposed);
        if (!plan.CanSave)
            return new RepairSaveOutcome(false, string.Join(" ", plan.Blockers));

        var movedTo = (string?)null;
        var movedFrom = (string?)null;

        // A move that succeeded but could NOT delete the old folder — a file held open at delete time,
        // the likeliest failure on this path since the game may be running. DataDirMove is right to
        // call that a success (the data is at the target and verified), but every message downstream
        // changes: a full duplicate of the user's disabled mods is still on the old volume, and the
        // reverse plan will refuse to merge onto it. Discarding this flag is what let the failure path
        // tell the user their mods were orphaned when a complete copy sat exactly where the unchanged
        // registration points.
        var sourceSurvived = false;

        if (plan.DataDir is { } move)
        {
            if (moveDataDir)
            {
                var result = await Task.Run(() => DataDirMove.Execute(move, progress));
                if (!result.Moved)
                    return new RepairSaveOutcome(false, result.Error ?? "The launcher data could not be moved.");
                movedFrom = move.From;
                movedTo = move.To;
                sourceSurvived = !result.SourceRemoved && move.Kind != DataDirMoveKind.Nothing;
            }
            else
            {
                // Pin: point the registration at where the data already is. Scanner.DataDirForGame
                // honours an explicit DataDir ahead of its derivation, so nothing moves at all. Core
                // owns which folder that is (stored, not proposed) — see PinDataDirTo.
                proposed.DataDir = plan.PinDataDirTo;
            }
        }

        if (plan.FieldsToPin.Count > 0) proposed.UserSet = plan.FieldsToPin;

        try
        {
            var reg = _svc.LoadRegistry();
            _svc.SaveRegistry(Registry.UpsertGame(reg, proposed));

            // READ IT BACK. games.json is a shared read-modify-write with no lock — StampLaunch,
            // SetActiveGame, Redetect and discovery all do LoadRegistry → change → SaveRegistry — so a
            // writer holding a stale snapshot can land after this one and restore the OLD GameRoot,
            // with the data already sitting at the new path. That is a silent orphan: the launcher
            // would then look for this game's disabled mods where they no longer are. Locking the file
            // properly is a backlog item; going silent about it is not acceptable in the one caller
            // where the cost is the user's only copy of their mods.
            var written = _svc.LoadRegistry().Games.FirstOrDefault(g => g.Id == proposed.Id);
            if (written is null
                || !PathEquals(written.GameRoot, proposed.GameRoot)
                || !PathEquals(written.DataDir, proposed.DataDir))
                return new RepairSaveOutcome(false, RegistrationRepairText.Clobbered(
                    written?.GameRoot, written?.DataDir, proposed.GameRoot, proposed.DataDir, movedTo));
        }
        catch (Exception e)
        {
            if (movedTo is null || movedFrom is null)
                return new RepairSaveOutcome(false,
                    ErrorRemedy.Describe(e, "Couldn't save your settings, so nothing was changed"));

            // The data is at the new location and the registration still points at the old one — the
            // orphaning this whole feature exists to prevent. Put it back. A fresh plan, not a stored
            // inverse, so the reverse gets the same refusals and free-space check as the forward trip.
            //
            // On a worker thread, like the forward trip. The forward await captured the UI
            // SynchronizationContext, so a bare synchronous call here would put gigabytes back with
            // the window frozen and the status stuck on the last forward tick — on the one operation
            // the design says must never look like a hang.
            var back = await Task.Run(() => DataDirMove.Execute(DataDirMove.Plan(movedTo, movedFrom), progress));
            if (back.Moved)
                return new RepairSaveOutcome(false,
                    ErrorRemedy.Describe(e, "Couldn't save your settings, so nothing was changed"));

            // THE OLD COPY MAY STILL BE THERE. CopyVerifyDelete reports SourceRemoved false when the
            // source could not be deleted — a file held open, which is the likeliest failure here since
            // the game may be running — and the reverse plan then refuses outright rather than merge
            // two data folders. Telling the user their mods are orphaned when a copy sits exactly where
            // the unchanged registration points is worse than saying nothing.
            //
            // But DO NOT CALL THE LEFTOVER A SPARE. Directory.Delete(recursive) removes children one at
            // a time, so a lock hit partway leaves the SOURCE partially deleted while the target is the
            // tree that was verified complete. Both halves of this compound failure — the write throwing
            // and the delete failing — have the same likeliest cause, the game running, so they arrive
            // together. Naming either copy disposable here can point the user at deleting the only
            // complete one. Name which is which and let them compare. The words live in Core, where a
            // test holds them to that.
            return new RepairSaveOutcome(false,
                RegistrationRepairText.SaveFailedAfterMove(movedFrom, movedTo, sourceSurvived));
        }

        // A move that could not delete the old copy is still a successful move — the data is at the
        // target and verified. Saying nothing would leave a duplicate of the user's disabled mods on
        // the old volume with no hint it is there. It is NOT called a spare: the recursive delete
        // removes children one at a time, so what survives a lock partway through may be a partial
        // tree, and "spare copy" invites treating it as a second complete one.
        return new RepairSaveOutcome(true, sourceSurvived && movedFrom is not null && movedTo is not null
            ? RegistrationRepairText.SavedOldFolderRemains(movedFrom, movedTo)
            : RegistrationRepairText.Saved);
    }

    // DataDirMove.Norm is internal to Core (visible only to the test assembly), so the read-back does
    // its own normalisation. Same shape: full path, no trailing separator, case-insensitive — a
    // registry round-trip must not read as a clobber because one side kept a trailing backslash.
    private static bool PathEquals(string? a, string? b)
        => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }
}
