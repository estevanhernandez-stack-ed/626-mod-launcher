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

    public RegistrationChangePlan Preview(GameEntry stored, GameEntry proposed)
        => RegistrationChange.Plan(stored, proposed);

    /// <summary>
    /// Apply an edit.
    ///
    /// <para>ORDER IS THE SAFETY. The data-dir move runs BEFORE the registry write, so a failed move
    /// leaves nothing written anywhere — registration untouched, data untouched. A failed write AFTER
    /// a successful move would orphan the user's only copy of their disabled mods, so that case
    /// re-plans the move in reverse and runs it. If the reverse also fails, both absolute paths go
    /// into the message: silence is the only unacceptable outcome.</para>
    /// </summary>
    public async Task<RepairSaveOutcome> SaveAsync(
        GameEntry stored, GameEntry proposed, bool moveDataDir, IProgress<(int Copied, int Total)>? progress)
    {
        var plan = Preview(stored, proposed);
        if (!plan.CanSave)
            return new RepairSaveOutcome(false, string.Join(" ", plan.Blockers));

        var movedTo = (string?)null;
        var movedFrom = (string?)null;

        if (plan.DataDir is { } move)
        {
            if (moveDataDir)
            {
                var result = await Task.Run(() => DataDirMove.Execute(move, progress));
                if (!result.Moved)
                    return new RepairSaveOutcome(false, result.Error ?? "The launcher data could not be moved.");
                movedFrom = move.From;
                movedTo = move.To;
            }
            else
            {
                // Pin: point the registration at where the data already is. Scanner.DataDirForGame
                // honours an explicit DataDir ahead of its derivation, so nothing moves at all.
                proposed.DataDir = Scanner.DataDirForGame(stored);
            }
        }

        if (plan.FieldsToPin.Count > 0) proposed.UserSet = plan.FieldsToPin;

        try
        {
            var reg = _svc.LoadRegistry();
            _svc.SaveRegistry(Registry.UpsertGame(reg, proposed));
        }
        catch (Exception e)
        {
            if (movedTo is null || movedFrom is null)
                return new RepairSaveOutcome(false, ErrorRemedy.Describe(e));

            // The data is at the new location and the registration still points at the old one — the
            // orphaning this whole feature exists to prevent. Put it back. A fresh plan, not a stored
            // inverse, so the reverse gets the same refusals and free-space check as the forward trip.
            var back = DataDirMove.Execute(DataDirMove.Plan(movedTo, movedFrom));
            return back.Moved
                ? new RepairSaveOutcome(false, "Nothing was changed. " + ErrorRemedy.Describe(e))
                : new RepairSaveOutcome(false,
                    $"Your settings could not be saved, and the launcher data could not be moved back. "
                    + $"It is at {movedTo}; this game still expects it at {movedFrom}.");
        }

        _svc.NotifyRegistryChanged();
        return new RepairSaveOutcome(true, "Saved.");
    }
}
