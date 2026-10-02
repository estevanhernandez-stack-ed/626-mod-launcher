namespace ModManager.Core;

/// <summary>
/// Turning a mod's name into a folder under one of 626's holding roots (<c>disabled</c>,
/// <c>disabled-trees</c>) without landing on a different mod's folder. Windows opens a path after
/// normalising it: a trailing dot or space is stripped (<c>Foo.</c> opens <c>Foo</c>), <c>..</c> climbs, and
/// an 8.3 alias (<c>OTHERL~1</c>) opens the long-named folder it abbreviates. A join of root and name is
/// therefore only the mod's own folder when the name is one folder as written AND the root lists an entry
/// by that real name. Uninstall's deletes ask here before touching a folder by name.
///
/// <para>626's own holding folders sidestep the normalisation entirely: they are named by
/// <see cref="HoldingName.Folder"/>, which encodes any name Windows would not keep as written. These checks
/// stay as the guards around a delete, applied to that encoded name.</para>
/// </summary>
internal static class FolderNames
{
    /// <summary>One path segment that Windows keeps exactly as written: no separators or other invalid
    /// characters (':' included, so no alternate stream), not "." or "..", and no trailing dot or space.</summary>
    public static bool NamesOneFolder(string name)
        => name.Length > 0 && name is not ("." or "..")
           && !name.EndsWith('.') && !name.EndsWith(' ')
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="parent"/> (both full paths).</summary>
    public static bool StrictlyUnder(string path, string parent)
    {
        var withSep = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return path.Length > withSep.Length && path.StartsWith(withSep, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the name, joined onto <paramref name="root"/> and resolved, leaves it: <c>..</c>,
    /// <c>.</c>, <c>..\x</c>, a rooted or drive-relative name, or one the resolver rejects. A genuine escape,
    /// as opposed to a name that only can't be its own folder.</summary>
    public static bool Escapes(string root, string name)
    {
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        try { return !StrictlyUnder(Path.GetFullPath(Path.Combine(full, name)), full); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return true; }
    }

    /// <summary>True when <paramref name="root"/> lists an entry whose real name is <paramref name="name"/>
    /// (case-insensitive, as Windows is). Listed without a search pattern, so an 8.3 alias never matches.</summary>
    public static bool HasEntryNamed(string root, string name)
        => Directory.Exists(root)
           && new DirectoryInfo(root).EnumerateFileSystemInfos()
               .Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The path to give the file system for an entry the scan enumerated by its real relative name. When a
    /// segment ends in a dot or space (only a <c>\\?\</c>-aware tool can create one), the extended-length form
    /// is returned, which Windows does not normalise, so the exact entry is reached rather than its lookalike.
    /// Otherwise the plain join.
    /// </summary>
    public static string ExactPath(string baseDir, string relative)
    {
        var joined = Path.Combine(baseDir, relative);
        var odd = relative.Split('\\', '/').Any(s => (s.EndsWith('.') && s is not ("." or "..")) || s.EndsWith(' '));
        if (!odd) return joined;
        // Resolve the base only (it has no odd segments) and append the relative part untouched.
        var full = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar
                   + relative.Replace('/', Path.DirectorySeparatorChar);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }
}
