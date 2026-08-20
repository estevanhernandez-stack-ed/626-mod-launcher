using System.Text.RegularExpressions;

namespace ModManager.Core.Transport;

/// <summary>
/// Which files inside a save are <b>you</b> rather than the place.
///
/// <para>The line a shareable bundle cuts along. Curated per game in the signed manifest as
/// <c>savePlayerPaths</c>, because it is a fact about the game and not guessable — Palworld keeps your
/// character in <c>Players/</c> and <c>LocalData.sav</c>, Windrose in <c>Accounts/</c> and
/// <c>Players/</c>, Minecraft buries it inside the world folder, and Cyberpunk has no such line at
/// all because there is no world half to keep.</para>
///
/// <para><b>Relative to the save UNIT, not the save folder.</b> When the game's layout is
/// <c>worlds</c>, a unit is one world folder and the patterns start inside it. Otherwise a unit is the
/// whole save folder. Palworld and Windrose are the two worked examples, one of each.</para>
///
/// <para><b>Absent means we have not curated it</b>, never "there is no character data". The caller
/// must not offer to share a world whose seam is unknown — a guessed split ships a stranger somebody's
/// character.</para>
/// </summary>
public static class SaveSeam
{
    /// <summary>
    /// Whether a path inside a save unit belongs to the player.
    ///
    /// <para>Glob, not a fixed vocabulary, and Windrose is why: its three folders repeat under
    /// <c>RocksDB/</c>, <c>RocksDB_v2/</c>, a nested migration tree and a backups tree. No enumerable
    /// list of names survives that; <c>**/Players/**</c> does.</para>
    /// </summary>
    public static bool IsPlayerPath(string? relativePath, IReadOnlyList<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0) return false;
        var rel = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
        if (rel.Length == 0) return false;

        foreach (var p in patterns)
            if (Matches(rel, p)) return true;
        return false;
    }

    /// <summary>Split a unit's files into what stays with the world and what is the player's.</summary>
    public static (IReadOnlyList<string> World, IReadOnlyList<string> Player) Split(
        IEnumerable<string> relativePaths, IReadOnlyList<string>? patterns)
    {
        var world = new List<string>();
        var player = new List<string>();
        foreach (var rel in relativePaths)
            (IsPlayerPath(rel, patterns) ? player : world).Add(rel);
        return (world, player);
    }

    private static readonly Dictionary<string, Regex> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    private static bool Matches(string rel, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        Regex rx;
        lock (Gate)
        {
            if (!Cache.TryGetValue(pattern!, out rx!))
                Cache[pattern!] = rx = new Regex(Translate(pattern!),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return rx.IsMatch(rel);
    }

    /// <summary>
    /// Glob to regex. <c>**</c> crosses directory separators, <c>*</c> does not, <c>?</c> is one
    /// character. Everything else is literal — a curated pattern is data from a feed, and letting a
    /// dot or a plus through as a regex metacharacter would silently widen what counts as "yours".
    /// </summary>
    private static string Translate(string glob)
    {
        var g = glob.Replace('\\', '/').TrimStart('/');
        var sb = new System.Text.StringBuilder("^");
        for (var i = 0; i < g.Length; i++)
        {
            var c = g[i];
            if (c == '*' && i + 1 < g.Length && g[i + 1] == '*')
            {
                // "**/" may also match nothing at all, so **/Players/** finds a top-level Players too.
                if (i + 2 < g.Length && g[i + 2] == '/') { sb.Append("(?:.*/)?"); i += 2; }
                else { sb.Append(".*"); i++; }
            }
            else if (c == '*') sb.Append("[^/]*");
            else if (c == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.Append('$').ToString();
    }
}
