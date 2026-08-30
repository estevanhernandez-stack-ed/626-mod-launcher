namespace ModManager.Core.Transport;

/// <summary>
/// Where an archive entry is allowed to land.
///
/// <para><b>A prefix check is not containment, and this existed as one twice.</b> Comparing
/// <c>dest.StartsWith(root)</c> accepts a SIBLING directory: with a root of <c>…/saves/pal</c>, the
/// path <c>…/saves/palworld-evil/x.sav</c> starts with the root string and is nowhere inside it. An
/// archive is a file from another machine, so an entry named <c>../palworld-evil/x.sav</c> is a write
/// wherever the attacker likes.</para>
///
/// <para>Asking for the RELATIVE path answers the real question. Anything that has to climb out says
/// so with a leading <c>..</c>, and anything on another root comes back rooted. No separator
/// arithmetic, no case rules to get wrong per platform.</para>
/// </summary>
public static class SafeExtractPath
{
    /// <summary>Whether <paramref name="candidate"/> is inside <paramref name="root"/>, or is the root
    /// itself. Both are resolved before comparing, so <c>..</c> and symlink-ish spellings collapse.</summary>
    public static bool IsInside(string root, string candidate)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(candidate)) return false;

        string fullRoot, fullDest;
        try
        {
            fullRoot = Path.GetFullPath(root);
            fullDest = Path.GetFullPath(candidate);
        }
        catch { return false; }   // an unresolvable path is not a safe one

        var rel = Path.GetRelativePath(fullRoot, fullDest);

        // Rooted means a different drive or share entirely; ".." means it climbed out. "." is the
        // root itself, which is inside by definition.
        if (Path.IsPathRooted(rel)) return false;
        if (rel == "..") return false;
        return !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !rel.StartsWith("../", StringComparison.Ordinal);
    }

    /// <summary>Resolve an entry's destination, or throw naming the entry. The message says the entry
    /// and not the resolved path, because the resolved path is the attacker's choice and echoing it
    /// into a UI is its own small gift.</summary>
    public static string ResolveOrThrow(string root, string relativeEntry)
    {
        var dest = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), relativeEntry));
        if (!IsInside(root, dest))
            throw new InvalidOperationException(
                $"This archive tries to write outside the folder it was given ({relativeEntry}). Refused.");
        return dest;
    }
}
