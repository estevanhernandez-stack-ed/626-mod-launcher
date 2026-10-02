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
        // A FRESH plan, never a preview's cached walk: this is the plan the move is executed from, so
        // its refusals (free space included) are the ones that decide. Off the UI thread: it walks the
        // whole data dir, and the caller awaits from the dispatcher.
        var plan = await Task.Run(() => Preview(stored, proposed));
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

        // The pins first: the journal below records the entry exactly as it will be saved, so a save an
        // interrupted launch finishes keeps the fields the user corrected pinned against the manifest.
        if (plan.FieldsToPin.Count > 0) proposed.UserSet = plan.FieldsToPin;

        if (plan.DataDir is { } move)
        {
            if (moveDataDir)
            {
                // The breadcrumb first (A6): if the launcher dies between this move and the registry
                // write, the next launch finds the record and finishes or reports the save.
                DataDirMoveJournal.Write(LauncherService.DataRoot,
                    new DataDirMoveRecord(proposed.Id, move.From, move.To, proposed, DateTime.UtcNow));
                var result = await Task.Run(() => DataDirMove.Execute(move, progress));
                if (!result.Moved)
                {
                    DataDirMoveJournal.Clear(LauncherService.DataRoot, proposed.Id);   // nothing moved
                    return new RepairSaveOutcome(false, result.Error ?? "The launcher data could not be moved.");
                }
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

        try
        {
            _svc.UpdateRegistry(reg => (Registry.UpsertGame(reg, proposed), 0));

            // READ IT BACK. Every writer now goes through one lock (RegistryStore.Update, A6), so another
            // writer can no longer interleave with this one. The read-back stays as the last word in the
            // one caller where a lost write costs the user's only copy of their mods: a hand-edited file,
            // or a launcher too old to take the lock, could still land after it.
            var written = _svc.LoadRegistry().Games.FirstOrDefault(g => g.Id == proposed.Id);
            if (written is null
                || !PathEquals(written.GameRoot, proposed.GameRoot)
                || !PathEquals(written.DataDir, proposed.DataDir))
                return new RepairSaveOutcome(false, RegistrationRepairText.Clobbered(
                    written?.GameRoot, written?.DataDir, proposed.GameRoot, proposed.DataDir,
                    movedFrom, movedTo, sourceSurvived));
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
            {
                DataDirMoveJournal.Clear(LauncherService.DataRoot, proposed.Id);   // put back: nothing to finish
                return new RepairSaveOutcome(false,
                    ErrorRemedy.Describe(e, "Couldn't save your settings, so nothing was changed"));
            }

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
        // sourceSurvived is only ever set in the block that assigns both paths.
        // Saved and read back: the move, if any, is finished. A record kept on any failure path above
        // stays for the next launch to assess.
        DataDirMoveJournal.Clear(LauncherService.DataRoot, proposed.Id);
        return new RepairSaveOutcome(true, sourceSurvived
            ? RegistrationRepairText.SavedOldFolderRemains(movedFrom!, movedTo!)
            : RegistrationRepairText.Saved);
    }

    /// <summary>
    /// Finish or report data-folder moves a previous launcher left mid-save (A6). Run once at startup,
    /// before the first load. Each record is assessed inside the registry lock, against the registry
    /// as it is then: a finished save is finished exactly as the user confirmed it; anything ambiguous
    /// is reported once and left alone. Synchronous file and registry work: call it off the UI thread.
    /// </summary>
    /// <returns>The status line to show, or null when there was nothing to say.</returns>
    public string? RecoverInterruptedMoves()
    {
        var root = LauncherService.DataRoot;
        var notes = new List<string>();
        foreach (var record in DataDirMoveJournal.Pending(root))
        {
            var (verdict, name) = _svc.UpdateRegistry(reg =>
            {
                var registered = reg.Games.FirstOrDefault(g => string.Equals(g.Id, record.GameId, StringComparison.OrdinalIgnoreCase));
                var v = DataDirMoveJournal.Assess(record, registered, DataDirMoveJournal.HasData);
                var label = registered?.GameName ?? record.Proposed.GameName;
                return v == MoveRecovery.FinishSave
                    ? (Registry.UpsertGame(reg, record.Proposed), (v, label))
                    : (reg, (v, label));
            });

            switch (verdict)
            {
                case MoveRecovery.FinishSave:
                    DataDirMoveJournal.Clear(root, record.GameId);
                    notes.Add(DataDirMoveJournal.FinishedMessage(record, name));
                    break;
                case MoveRecovery.NeedsYou:
                    // Said once, then forgotten: the folders are untouched and the game keeps the one it
                    // uses. A record kept until the user acts would come back every launch for a choice
                    // (keep the source) that needs no action at all.
                    DataDirMoveJournal.Clear(root, record.GameId);
                    notes.Add(DataDirMoveJournal.NeedsYouMessage(record, name));
                    break;
                default:
                    DataDirMoveJournal.Clear(root, record.GameId);
                    break;
            }
        }
        return notes.Count == 0 ? null : string.Join(" ", notes);
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
