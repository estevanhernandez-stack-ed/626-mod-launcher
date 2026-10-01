namespace ModManager.Core;

/// <summary>
/// The words a registration save reports back, for every outcome that involves a moved data dir.
///
/// <para>These are the highest-stakes strings in the registration-repair feature. Each one is read by
/// a user deciding whether to delete a folder that may hold the only complete copy of their disabled
/// mods, so each one has to say exactly where the data is, where the game expects it, and which copy
/// was verified. They lived inline in the App's <c>RegistrationRepairService</c>, where no test could
/// reach them; here, <c>RegistrationRepairTextTests</c> holds each to the facts it must state.</para>
///
/// <para>One rule runs through all of them: NEVER CALL A LEFTOVER COPY A SPARE. A recursive delete
/// removes children one at a time, so a lock hit partway leaves the old folder partly deleted while
/// the new one is the tree that was verified complete. Naming either copy disposable can point the
/// user at deleting the only complete one. Name which is which and let them compare.</para>
/// </summary>
public static class RegistrationRepairText
{
    /// <summary>The save landed and nothing needs saying beyond that.</summary>
    public const string Saved = "Saved.";

    /// <summary>
    /// The move succeeded and the save landed, but the old folder could not be removed — a file held
    /// open at delete time, the likeliest failure on this path since the game may be running. Still a
    /// success: the data is at the target and verified. Saying nothing would leave a duplicate of the
    /// user's disabled mods on the old volume with no hint it is there.
    /// </summary>
    public static string SavedOldFolderRemains(string movedFrom, string movedTo)
        => $"Saved. The old launcher data folder at {movedFrom} could not be removed and may be "
           + $"partly deleted; this game now reads its data from {movedTo}, which was verified "
           + "complete, so check the old folder before you remove it.";

    /// <summary>
    /// The data moved, the save then failed, and the move back did not complete. Two very different
    /// situations, told apart by <paramref name="sourceSurvived"/>:
    /// <list type="bullet">
    /// <item><b>The old folder survived</b> (the forward move could not delete it). The registration is
    /// unchanged and still points at it, so nothing about the game changed — but the old copy may be
    /// partial, and the new one is the verified one. Telling this user their mods are orphaned would be
    /// worse than saying nothing.</item>
    /// <item><b>It did not.</b> The data is only at <paramref name="movedTo"/> and the game still expects
    /// it at <paramref name="movedFrom"/>. Both absolute paths go into the message: silence is the only
    /// unacceptable outcome.</item>
    /// </list>
    /// </summary>
    public static string SaveFailedAfterMove(string movedFrom, string movedTo, bool sourceSurvived)
        => sourceSurvived
            ? "Your settings could not be saved, so nothing about this game changed, and its "
              + $"launcher data is still at {movedFrom} where this game expects to find it. The "
              + $"copy at {movedTo} is the one that was verified complete, so compare the two "
              + "before you remove either."
            : "Your settings could not be saved, and the launcher data could not be moved back. "
              + $"It is at {movedTo}; this game still expects it at {movedFrom}.";

    /// <summary>
    /// The save landed and was then overwritten by another writer holding a stale snapshot of
    /// <c>games.json</c> — the read-back caught it. Says what the game now reads as, what was asked
    /// for, and — when data was moved — where it is now, because that is the case where carrying on
    /// would have the launcher look for this game's disabled mods where they no longer are.
    /// </summary>
    /// <param name="writtenGameRoot">The game folder the registry holds after the read-back, or null
    /// when the game is missing from it altogether.</param>
    /// <param name="writtenDataDir">The data dir the registry holds after the read-back.</param>
    /// <param name="movedTo">Where the data was moved this save, or null when nothing moved.</param>
    public static string Clobbered(
        string? writtenGameRoot, string? writtenDataDir,
        string? proposedGameRoot, string? proposedDataDir,
        string? movedTo)
        => "Your settings were saved and then changed back by something else running at the "
           + $"same time. This game now reads as being at {Describe(writtenGameRoot)} with its "
           + $"launcher data at {Describe(writtenDataDir)}; you asked for "
           + $"{Describe(proposedGameRoot)} and {Describe(proposedDataDir)}. "
           + (movedTo is null
               ? "Nothing was moved. Open the setup again and re-apply the change."
               : $"This game's launcher data has already been moved to {movedTo}, so open the "
                 + "setup again and re-apply the change before using this game.");

    private static string Describe(string? path) => string.IsNullOrWhiteSpace(path) ? "not set" : path;
}
