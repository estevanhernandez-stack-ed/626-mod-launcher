namespace ModManager.Core;

/// <summary>
/// The write half of the save-type offer: snapshot the whole save folder, then copy the chosen save over the target.
///
/// <para>The snapshot is a <b>user</b> snapshot, not an auto one, so retention never prunes it. The launch that follows
/// takes its own auto snapshot and prunes to the keep count (as low as 1), and that would otherwise delete the only copy
/// of the save the player just replaced (reversibility audit, 2026-11-07). It is taken even when the target doesn't
/// exist yet, so every copy has a one-click undo: a full restore of this snapshot removes the copied file.</para>
///
/// <para>The source is only read. The copy itself is temp-then-rename (<see cref="SaveManager.CloneToType"/>).</para>
/// </summary>
public static class SaveOfferCopy
{
    public const string SnapshotLabel = "before-clone";

    /// <summary>Returns the snapshot (the undo) and the file name written. Throws when either step fails; when the
    /// snapshot fails, nothing has been copied.</summary>
    public static (SaveSnapshot Snapshot, string Written) Run(string saveDir, string snapshotsDir, SaveOfferPlan plan, SaveOfferSource source)
    {
        var snapshot = SaveManager.Backup(saveDir, snapshotsDir, SnapshotLabel, auto: false);
        var written = SaveManager.CloneToType(saveDir, source.File.FileName, plan.Target.Extension, overwrite: true);
        return (snapshot, written);
    }
}
