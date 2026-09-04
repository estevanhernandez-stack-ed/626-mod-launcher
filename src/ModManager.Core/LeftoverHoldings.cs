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
            if (!string.IsNullOrEmpty(parent)) roots.Add(parent);
        }

        var ids = registered.Select(g => g.Id).Where(id => !string.IsNullOrEmpty(id));
        var found = new List<LeftoverHolding>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            var names = Directory.GetDirectories(root)
                .Select(System.IO.Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();

            foreach (var name in Orphans(ids, names))
            {
                var path = System.IO.Path.Combine(root, name);
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                var top = Directory.GetFileSystemEntries(path)
                    .Select(System.IO.Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                found.Add(new LeftoverHolding(
                    path, name, files.Length,
                    files.Sum(f => new FileInfo(f).Length), top));
            }
        }

        return found.OrderBy(h => h.FolderName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
