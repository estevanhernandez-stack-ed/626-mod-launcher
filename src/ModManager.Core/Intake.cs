namespace ModManager.Core;

/// <summary>
/// Pure drop-file classification. 'zip' -> extract &amp; route mod entries (any supported archive
/// format — see <see cref="ArchiveExtensions"/>); 'mod' -> place directly; 'skip' -> ignore.
/// Mirrors intake-core.js. (The extraction + folder walk with the path-traversal guard live in
/// Scanner / DirectInject, where the IO is, behind the ArchiveReader seam.)
/// </summary>
public static class Intake
{
    /// <summary>Archive containers routed through the ArchiveReader seam (SharpCompress reads all).
    /// The classifier still labels them "zip" so existing intake branches stay unchanged.</summary>
    public static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar" };

    public static string ClassifyDrop(string filePath, IEnumerable<string>? exts)
    {
        var lower = filePath.ToLowerInvariant();
        if (ArchiveExtensions.Any(a => lower.EndsWith(a))) return "zip";
        var dot = lower.LastIndexOf('.');
        var ext = dot >= 0 ? lower[(dot + 1)..] : lower;
        var set = (exts ?? Enumerable.Empty<string>()).Select(e => e.ToLowerInvariant());
        return set.Contains(ext) ? "mod" : "skip";
    }
}
