namespace ModManager.Core;

/// <summary>
/// Deleting a folder tree without ever stepping through a link. A junction or symlink inside the tree is
/// removed as a link, and whatever it points at is never touched. The same rule as
/// <see cref="SafeMove"/>'s link refusal (a reparse point is not walked into), applied to a delete:
/// <c>Directory.Delete(recursive)</c> is not used, because what a recursive delete does with a link is
/// the one thing this class exists so nobody has to remember.
/// </summary>
internal static class LinkSafeDelete
{
    /// <summary>True for a junction, a symlink, or any other reparse point.</summary>
    public static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    /// <summary>
    /// Delete <paramref name="root"/> and everything under it. Files one at a time, then folders deepest
    /// first, each non-recursively. A link at any level, the root included, is deleted as the link itself:
    /// <c>Directory.Delete(path)</c> for a folder link, <c>File.Delete</c> for a file link. A missing root
    /// is nothing to do. Failures surface; the caller verifies what is left.
    /// </summary>
    public static void DeleteTree(string root)
    {
        var top = new DirectoryInfo(root);
        if (!top.Exists)
        {
            var asFile = new FileInfo(root);
            if (asFile.Exists) DeleteEntry(asFile);
            return;
        }
        if (IsLink(top)) { DeleteEntry(top); return; }

        // Folders in the order they were reached, so walking the list backwards is deepest first.
        var folders = new List<DirectoryInfo> { top };
        var pending = new Stack<DirectoryInfo>();
        pending.Push(top);
        while (pending.Count > 0)
            foreach (var entry in pending.Pop().EnumerateFileSystemInfos())
            {
                if (IsLink(entry) || entry is FileInfo) DeleteEntry(entry);
                else if (entry is DirectoryInfo d) { folders.Add(d); pending.Push(d); }
            }

        for (var i = folders.Count - 1; i >= 0; i--)
        {
            folders[i].Attributes &= ~FileAttributes.ReadOnly;
            Directory.Delete(folders[i].FullName);   // non-recursive: empty, or it throws
        }
    }

    /// <summary>True when anything other than an empty folder is at or under <paramref name="root"/>: a
    /// file, or a link (not followed). The check after a delete.</summary>
    public static bool HoldsAnything(string root)
    {
        var top = new DirectoryInfo(root);
        if (!top.Exists) return File.Exists(root);
        if (IsLink(top)) return true;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(top);
        while (pending.Count > 0)
            foreach (var entry in pending.Pop().EnumerateFileSystemInfos())
            {
                if (IsLink(entry) || entry is FileInfo) return true;
                if (entry is DirectoryInfo d) pending.Push(d);
            }
        return false;
    }

    // One entry: a file or a link. A link to a folder is a directory entry, and a non-recursive
    // Directory.Delete removes the link without reading what it points at.
    private static void DeleteEntry(FileSystemInfo entry)
    {
        if (entry is DirectoryInfo)
        {
            // A read-only folder link refuses a delete. Clearing the flag on the link entry changes the
            // link's own attributes, not its target's (a test pins this).
            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                entry.Attributes &= ~FileAttributes.ReadOnly;
            Directory.Delete(entry.FullName);
            return;
        }
        // Copied game files can be read-only. A file link's attributes are left alone: setting them is one more
        // call that could reach the target.
        if (!IsLink(entry) && entry.Attributes.HasFlag(FileAttributes.ReadOnly))
            entry.Attributes &= ~FileAttributes.ReadOnly;
        File.Delete(entry.FullName);
    }
}
