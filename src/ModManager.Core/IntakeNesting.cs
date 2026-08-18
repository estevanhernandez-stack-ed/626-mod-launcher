namespace ModManager.Core;

/// <summary>
/// Where inside a mod folder an archive entry belongs.
///
/// <para>Intake used to place every entry by its bare filename, so a mod shipping a tree landed flat.
/// For a pak game that is correct and must stay: a pak is one file and its name is its identity. For a
/// script tree it is destructive — CatLib is 33 lua files under <c>autorun/_CatLib/</c>, and flattened
/// none of them resolve; two mods each shipping <c>autorun/utility/Statics.lua</c> collide on one name
/// and the second silently wins. The install reports success and does not work.</para>
///
/// <para>The placement is derivable rather than guessed, because the archives say it themselves: an RE
/// Engine mod zip carries a full <c>reframework/autorun/...</c> path inside. Find the declared mod
/// location in the entry path and keep everything below it.</para>
/// </summary>
public static class IntakeNesting
{
    /// <summary>
    /// The path an entry should take under the mod folder, or null when the archive says nothing and
    /// the caller should fall back to the bare filename.
    ///
    /// <param name="entryPath">The archive-internal path, e.g. <c>KittyBig-v1.0/reframework/autorun/KittyBig.lua</c>.</param>
    /// <param name="anchorRelative">The mod location relative to the game root, e.g. <c>reframework/autorun</c>.</param>
    /// </summary>
    public static string? RelativeUnderAnchor(string? entryPath, string? anchorRelative)
    {
        var entry = Split(entryPath);
        var anchor = Split(anchorRelative);
        if (entry.Length == 0 || anchor.Length == 0) return null;

        // LAST match, not the first. A mod whose folder shares a name with a path segment would
        // otherwise anchor too early and keep a duplicate level - _CatLib/reframework/autorun/_CatLib
        // must yield _CatLib/action.lua, never reframework/autorun/_CatLib/action.lua.
        for (var start = entry.Length - anchor.Length; start >= 0; start--)
        {
            var match = true;
            for (var i = 0; i < anchor.Length && match; i++)
                match = string.Equals(entry[start + i], anchor[i], StringComparison.OrdinalIgnoreCase);
            if (!match) continue;

            var below = entry.Skip(start + anchor.Length).ToArray();
            // The entry IS the anchor directory, or a file directly named like it - nothing below to place.
            if (below.Length == 0) return null;

            // ZIP SLIP. Preserving the archive's path means preserving whatever it says, and a
            // malicious entry says "reframework/autorun/../../../../Windows/System32/evil.dll". The
            // previous behaviour was accidentally safe: Path.GetFileName threw every directory
            // component away, so traversal could not survive. Keeping the tree removes that accident,
            // so the refusal has to be explicit.
            //
            // Refuse rather than sanitise. A path with a traversal segment is not a path we
            // misunderstood, it is an archive doing something it has no reason to do, and silently
            // rewriting it to something "safe" would install a file the author never described.
            // Returning null puts the caller back on the filename-only fallback, which cannot escape.
            if (below.Any(seg => seg == ".." || seg == ".")) return null;

            return string.Join(Path.DirectorySeparatorChar, below);
        }
        return null;
    }

    private static string[] Split(string? path)
        => (path ?? "").Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
