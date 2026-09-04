namespace ModManager.Core;

/// <summary>One holding folder that belongs to no registered game, described well enough for a user
/// to decide what to do with it. TopLevelNames matters because the folder is NOT only mods — it also
/// holds profiles, classification and metadata, and a UI that calls it "mods" invites someone to
/// delete a profile they wanted.</summary>
public sealed record LeftoverHolding(string Path, string FolderName, int FileCount, long Bytes,
                                     IReadOnlyList<string> TopLevelNames);

/// <summary>
/// The holding folders left behind when a game is removed from the launcher. Disabling a mod moves
/// its files to <c>&lt;library&gt;/_626mods/&lt;game-id&gt;/</c>; removing the game leaves that folder
/// referenced by nothing and shown nowhere, which sits badly next to a promise to keep your files.
/// </summary>
public static class LeftoverHoldings
{
    /// <summary>Pure: which folder names belong to no registered game. The whole judgment, with no
    /// filesystem in it.</summary>
    public static IReadOnlyList<string> Orphans(
        IEnumerable<string> registeredIds, IEnumerable<string> folderNames)
    {
        var known = new HashSet<string>(registeredIds, StringComparer.OrdinalIgnoreCase);
        return folderNames.Where(n => !known.Contains(n)).ToList();
    }

    /// <summary>Walks the holding roots the registered games point at and describes what Orphans
    /// picked out. Roots come from the games themselves, never from scanning drives — so a folder
    /// this app did not create cannot appear here.</summary>
    public static IReadOnlyList<LeftoverHolding> Find(IReadOnlyList<GameEntry> registered)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in registered)
        {
            var parent = System.IO.Path.GetDirectoryName(Scanner.DataDirForGame(g));
            // DataDirForGame returns game.DataDir verbatim when it's set, and DataDir comes out of
            // hand-editable games.json with no shape validation. Without this gate, a DataDir that
            // merely points at an ordinary folder turns every sibling inside it into an offer to
            // permanently delete it.
            if (!string.IsNullOrEmpty(parent)
                && string.Equals(System.IO.Path.GetFileName(parent), "_626mods", StringComparison.OrdinalIgnoreCase))
                roots.Add(parent);
        }

        // The known set is game ids, but a hand-edited games.json can point DataDir at a leaf name
        // that is NOT the game's id while still living inside a real _626mods root — nothing the app
        // itself writes produces that shape (RegistrationRepairService assigns
        // Scanner.DataDirForGame(stored), so leaf == id), but the registry is user-editable and this
        // is the same threat the root gate above already accepts, just incompletely closed. Union the
        // actual leaf names in with the ids so a live data folder can never be offered as an orphan,
        // whichever name the registry currently uses for it. This also subsumes the "game" substitution
        // DataDirForGame makes for an empty Id — no separate special-case needed.
        var ids = registered.Select(g => string.IsNullOrEmpty(g.Id) ? "game" : g.Id)
            .Concat(registered.Select(g => System.IO.Path.GetFileName(
                Scanner.DataDirForGame(g).TrimEnd(
                    System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))))
            .Where(n => !string.IsNullOrEmpty(n))!;
        var found = new List<LeftoverHolding>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            // Directory.Exists only proves the root can be stat'd, not listed — an ACL-restricted
            // root throws on GetDirectories and would take out Find entirely, hiding every leftover
            // instead of just this root's. Same narrow filter as the per-folder reads below.
            string[] entries;
            try
            {
                entries = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var names = entries
                .Select(System.IO.Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            foreach (var name in Orphans(ids, names))
            {
                var path = System.IO.Path.Combine(root, name);

                // A subfolder can be ACL-restricted or vanish mid-scan (a real race while mods are
                // being toggled). Without this, one bad leftover throws out of Find and hides every
                // other leftover in the list, not just its own.
                string[] files;
                try
                {
                    files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var top = Directory.GetFileSystemEntries(path)
                    .Select(System.IO.Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                found.Add(new LeftoverHolding(
                    path, name, files.Length,
                    files.Sum(SafeLength), top));
            }
        }

        return found.OrderBy(h => h.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // A file can be deleted between the enumeration above and this read (another real race with mods
    // being toggled live) — FileInfo.Length throws FileNotFoundException in that case. Count it as
    // weightless rather than letting one vanished file take the whole listing down with it.
    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
