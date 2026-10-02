namespace ModManager.Core;

/// <summary>
/// When the save folder a game USES should be the curated one rather than the stored one.
///
/// <para><b>The case.</b> Before the feed named a save folder for EA's football games, adding one fell
/// back to the folder guess, which finds <c>Documents\&lt;title&gt;</c>: the PARENT of <c>saves</c>. That
/// folder exists, so it was stored, and a stored folder that exists is never detected again. Listing by
/// name looks at the top level only, so the panel showed no saves, and a curated hint added later could
/// never reach the game.</para>
///
/// <para><b>Read time, never stored.</b> The answer feeds the context (<c>Scanner.GameContext</c>'s
/// <c>effectiveSaveDir</c>); the registry keeps what it had. So nothing here is permanent: when any
/// condition below stops holding, the game is back on its stored folder with nothing to undo.</para>
///
/// <para><b>Every condition must hold:</b></para>
/// <list type="bullet">
/// <item>The launcher never writes this game's saves (<see cref="SaveWritePolicy"/>). A snapshot is rooted at
/// the folder it was taken from, and a game that can restore would put an old parent-rooted snapshot back
/// one level down as <c>saves\saves</c>.</item>
/// <item>The user did not pick the stored folder (<see cref="GameEntry.UserSetSaveDir"/>).</item>
/// <item>The curated hint names no store account (<c>&lt;storeUserId&gt;</c>): that is one player's
/// subfolder, and narrowing to it would show a shared PC's other players somebody else's saves.</item>
/// <item>The stored folder is the curated folder's DIRECT parent: the guess stopped exactly one level
/// too high. Anything else is a choice, not a guess to correct.</item>
/// <item>The curated folder exists.</item>
/// </list>
/// </summary>
public static class SaveDirRefresh
{
    /// <summary>The curated folder to use instead of the stored one, or null to use what is stored.</summary>
    /// <param name="curated">The game's curated save folder, already resolved to a real path.</param>
    public static string? Narrowed(GameEntry game, string? curated, Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        if (SaveWritePolicy.Refusal(game) is null) return null;
        if (game.UserSet?.Contains(GameEntry.UserSetSaveDir, StringComparer.OrdinalIgnoreCase) == true) return null;
        if (SaveDirHints.For(game)?.Contains("<storeUserId>", StringComparison.OrdinalIgnoreCase) == true) return null;
        var stored = game.SaveDir;
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(curated)) return null;

        string fullStored, parentOfCurated;
        try
        {
            fullStored = Trim(Path.GetFullPath(stored));
            parentOfCurated = Trim(Path.GetDirectoryName(Trim(Path.GetFullPath(curated))) ?? "");
        }
        catch { return null; }   // an unresolvable path is not one to move a game to

        // Case-insensitive, as Windows paths are: the folders this exists for live there.
        if (!string.Equals(fullStored, parentOfCurated, StringComparison.OrdinalIgnoreCase)) return null;
        return exists(curated) ? curated : null;
    }

    private static string Trim(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
