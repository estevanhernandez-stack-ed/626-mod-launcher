namespace ModManager.Core;

/// <summary>
/// When a stored save folder should give way to the curated one.
///
/// <para><b>The case.</b> Before the feed named a save folder for EA's football games, adding one fell
/// back to the folder guess, which finds <c>Documents\&lt;title&gt;</c>: the PARENT of <c>saves</c>. That
/// folder exists, so it was stored, and a stored folder that exists is never detected again. Listing by
/// name looks at the top level only, so the panel showed no saves, and a curated hint added later could
/// never reach the game.</para>
///
/// <para><b>The rule.</b> Move to the curated folder only when the stored one CONTAINS it: the guess
/// stopped one level too high, and narrowing to the folder the feed names is a correction, not a
/// different choice. A stored folder anywhere else is the user's or Ludusavi's, and is kept.</para>
///
/// <para><b>Only where nothing restores.</b> A snapshot is rooted at the save folder it was taken from.
/// Narrowing the folder under a game that can restore would put an old parent-rooted snapshot back one
/// level down, nesting <c>saves\saves</c>. So this applies only to games whose saves the launcher never
/// writes (<see cref="SaveWritePolicy"/>); every other game keeps today's behaviour exactly.</para>
/// </summary>
public static class SaveDirRefresh
{
    /// <summary>The curated folder to store instead, or null to keep what is stored.</summary>
    /// <param name="curated">The game's curated save folder, already resolved to a real path.</param>
    public static string? Narrowed(GameEntry game, string? curated, Func<string, bool>? exists = null)
    {
        exists ??= Directory.Exists;
        if (SaveWritePolicy.Refusal(game) is null) return null;
        var stored = game.SaveDir;
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(curated)) return null;
        if (!exists(curated)) return null;

        var s = Normalise(stored);
        var c = Normalise(curated);
        return s.Length > 0 && c.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase) ? curated : null;
    }

    // Separators and a trailing slash are spelling, not location; case too, on the platform this runs on.
    private static string Normalise(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');
}
