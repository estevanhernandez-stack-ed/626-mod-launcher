using System.IO;
using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.App.Services;

/// <summary>
/// The I/O half of discovery: enumerate a root into relative paths, hand them to the pure
/// classifier, and hash archives on request. READ-ONLY — this service never writes, moves, or
/// deletes anything. Depth-capped so a deep game tree can't stall the UI.
/// </summary>
public sealed class DiscoveryScanService
{
    private const int MaxDepth = 6;
    private const int MaxFiles = 20000;

    /// <summary>Enumerate + classify. Unreadable folders are skipped, never fatal.</summary>
    public IReadOnlyList<DiscoveryCandidate> Sweep(string root, DiscoverySweepOptions options)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return Array.Empty<DiscoveryCandidate>();

        var files = new List<SweptFile>();
        Walk(root, root, 0, files);
        return DiscoverySweep.Classify(files, options);
    }

    private static void Walk(string root, string dir, int depth, List<SweptFile> into)
    {
        if (depth > MaxDepth || into.Count >= MaxFiles) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (into.Count >= MaxFiles) return;
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                // Size feeds Classify's base-game-pak guard on a paks-root mod path. A file that
                // vanishes between enumeration and this read (rare race) degrades to size 0 rather
                // than aborting the walk — worst case it reads as "small enough to be a mod",
                // exactly the shipping-name check's own conservative default already covers.
                long size;
                try { size = new FileInfo(file).Length; } catch { size = 0; }
                into.Add(new SweptFile(rel, size));
            }
            foreach (var sub in Directory.EnumerateDirectories(dir))
                Walk(root, sub, depth + 1, into);
        }
        catch (UnauthorizedAccessException) { /* skip locked folders — never fatal */ }
        catch (IOException) { /* same */ }
    }

    /// <summary>MD5 of a discovered archive for Nexus md5 lookup, or null if unreadable.
    /// Only meaningful for <see cref="DiscoveryKind.Archive"/> — Nexus hashes published
    /// archives, so extracted files never match.</summary>
    public string? Md5Of(string root, DiscoveryCandidate candidate)
    {
        if (candidate.Kind != DiscoveryKind.Archive) return null;
        try { return Md5Hash.OfFile(Path.Combine(root, candidate.RelativePath)); }
        catch { return null; }
    }
}
