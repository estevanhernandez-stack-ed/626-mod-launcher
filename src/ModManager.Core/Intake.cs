namespace ModManager.Core;

/// <summary>
/// Pure drop-file classification. 'zip' -> extract &amp; route mod entries; 'mod' -> place
/// directly; 'skip' -> ignore. Mirrors intake-core.js. (The zip extraction + folder walk
/// with the path-traversal guard live in Scanner, where the IO is.)
/// </summary>
public static class Intake
{
    public static string ClassifyDrop(string filePath, IEnumerable<string>? exts)
    {
        var lower = filePath.ToLowerInvariant();
        if (lower.EndsWith(".zip")) return "zip";
        var dot = lower.LastIndexOf('.');
        var ext = dot >= 0 ? lower[(dot + 1)..] : lower;
        var set = (exts ?? Enumerable.Empty<string>()).Select(e => e.ToLowerInvariant());
        return set.Contains(ext) ? "mod" : "skip";
    }
}
