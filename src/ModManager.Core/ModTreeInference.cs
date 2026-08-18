namespace ModManager.Core;

/// <summary>What an inferred group turned out to be.</summary>
public enum InferredKind
{
    /// <summary>A single script with nothing else beside it. `KittyBig.lua`.</summary>
    Script,
    /// <summary>A script plus a folder of the same name — one mod, two artifacts.</summary>
    ScriptWithFolder,
    /// <summary>A folder no script claims. Almost always a library other mods consume.</summary>
    Library,
}

/// <summary>One mod (or library) inferred from the shape of the mod folder, not from a record.</summary>
public sealed record InferredMod(
    string Key,
    string DisplayName,
    InferredKind Kind,
    IReadOnlyList<string> Members,
    bool Enabled = true)
{
    /// <summary>A library is shown but never given a toggle. We cannot switch off something other
    /// mods consume on the strength of a guess about who consumes it — the row explains instead.
    /// See the mod-provenance design.</summary>
    public bool Togglable => Kind != InferredKind.Library;
}

/// <summary>
/// Groups what is already in a mod folder into rows, for the files no install record claims.
///
/// <para>The inferred half of the mod-provenance design. 626 listed a mod per FILE, which is right
/// for a pak game and wrong for a script tree: on a real Monster Hunter Wilds install it listed six
/// lua files and silently ignored `_CatLib/` (33 files), `mhwilds_overlay/` and `utility/` — so a mod
/// appeared as half of itself and a library other mods depend on appeared not at all.</para>
///
/// <para>Best-effort by construction, and the row is expected to say so. Anything an install record
/// claims never reaches here: a tracked mod knows its own files exactly, and inference is only for
/// what arrived some other way.</para>
/// </summary>
public static class ModTreeInference
{
    /// <summary>
    /// Group the mod folder's top level.
    ///
    /// <param name="topLevelFiles">File names directly in the mod location.</param>
    /// <param name="topLevelDirs">Directory names directly in the mod location.</param>
    /// <param name="claimedRelPaths">Paths already claimed by install manifests. A top-level entry
    /// every one of whose members is claimed belongs to a tracked mod and is not inferred.</param>
    /// </summary>
    public static IReadOnlyList<InferredMod> Group(
        IEnumerable<string>? topLevelFiles,
        IEnumerable<string>? topLevelDirs,
        IEnumerable<string>? claimedRelPaths = null,
        IEnumerable<string>? disabledKeys = null)
    {
        var files = Clean(topLevelFiles);
        var dirs = Clean(topLevelDirs);

        // Disabling MOVES a mod's files to the holding folder, so reading the mod location alone sees
        // a disabled mod as absent - and a disabled PAIRED mod as an orphan folder, which rule 3 would
        // then call a library and refuse to switch. That strands it: the row a user needs in order to
        // turn it back on is the row that disappeared. Found by running this over a real tree where
        // mhwilds_overlay.lua sat in holding and mhwilds_overlay/ did not.
        //
        // Same principle the proxy-loader row already follows: a row that vanishes when disabled is a
        // one-way door in a launcher whose whole promise is that nothing is.
        var disabled = new HashSet<string>(Clean(disabledKeys), StringComparer.OrdinalIgnoreCase);

        // A claim on "_CatLib/action.lua" means the whole _CatLib top-level entry is spoken for. Match
        // on the first segment so one claimed file inside a tracked mod's folder is enough.
        //
        // Both spellings, because the two things compared against this set are spelled differently: an
        // on-disk entry is "KittyBig.lua" and a holding-folder key is "KittyBig". Storing only one form
        // let a tracked mod reappear as an inferred row the moment it was disabled.
        var claimedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seg in Clean(claimedRelPaths).Select(FirstSegment).Where(x => x.Length > 0))
        {
            claimedRoots.Add(seg);
            claimedRoots.Add(Path.GetFileNameWithoutExtension(seg));
        }

        var pairedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<InferredMod>();
        var seenStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (claimedRoots.Contains(file)) continue;
            var stem = Path.GetFileNameWithoutExtension(file);
            seenStems.Add(stem);

            // Rule 1: a script and a folder sharing a stem are ONE mod. This is the case the old
            // listing got visibly wrong - mhwilds_overlay.lua and mhwilds_overlay/ showed as one row
            // and one invisible folder, so turning the row off left most of the mod in place.
            var folder = dirs.FirstOrDefault(d => string.Equals(d, stem, StringComparison.OrdinalIgnoreCase));
            if (folder is not null)
            {
                pairedDirs.Add(folder);
                rows.Add(new InferredMod(stem, stem, InferredKind.ScriptWithFolder, new[] { file, folder }));
                continue;
            }

            // Rule 2: a bare script is a mod.
            rows.Add(new InferredMod(stem, stem, InferredKind.Script, new[] { file }));
        }

        // A disabled mod still gets a row, reading off. Its folder (if it left one behind) pairs the
        // same way it would have when enabled, so turning it back on is one click rather than a hunt
        // through the holding folder.
        foreach (var key in disabled)
        {
            if (claimedRoots.Contains(key) || seenStems.Contains(key)) continue;
            var folder = dirs.FirstOrDefault(d => string.Equals(d, key, StringComparison.OrdinalIgnoreCase));
            if (folder is not null) pairedDirs.Add(folder);
            rows.Add(new InferredMod(
                key, key,
                folder is not null ? InferredKind.ScriptWithFolder : InferredKind.Script,
                folder is not null ? new[] { key, folder } : new[] { key },
                Enabled: false));
        }

        // Rule 3: a folder no script claims is a library. Shown, never switched - the row's value is
        // what it explains about who needs it, which the caller fills in from a dependency scan.
        foreach (var dir in dirs)
        {
            if (pairedDirs.Contains(dir) || claimedRoots.Contains(dir)) continue;
            rows.Add(new InferredMod(dir, dir, InferredKind.Library, new[] { dir }));
        }

        return rows;
    }

    private static string FirstSegment(string p)
        => p.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    private static List<string> Clean(IEnumerable<string>? xs)
        => (xs ?? Enumerable.Empty<string>())
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToList();
}
