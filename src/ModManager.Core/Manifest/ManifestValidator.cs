namespace ModManager.Core.Manifest;

/// <summary>Result of validating a manifest: the surviving entries, plus what was dropped and why.</summary>
public sealed record ManifestValidationResult(
    GameManifest Manifest,
    IReadOnlyList<string> SkippedUnknownEngines,
    IReadOnlyList<string> RejectedEntries);

/// <summary>
/// Pure validation gate for a manifest from any source (embedded or, later, remote). Two rules:
///  - An entry whose non-null engine is unknown to this binary is SKIPPED (forward-compat: an old
///    binary reading a newer manifest simply doesn't see games it can't handle). Null engine is fine.
///  - An entry with an unsafe ModPath (absolute / drive-qualified / contains a ".." segment) is
///    REJECTED. ModPath is the one trust-sensitive field; the forbidden-paths gate at intake is the
///    downstream backstop, this is defense in depth.
/// </summary>
public static class ManifestValidator
{
    public static ManifestValidationResult Validate(GameManifest manifest, IReadOnlySet<string> knownEngines)
    {
        var kept = new List<GameManifestEntry>();
        var skipped = new List<string>();
        var rejected = new List<string>();

        foreach (var g in manifest.Games)
        {
            if (g.Engine is { } engine && !knownEngines.Contains(engine))
            {
                skipped.Add(g.Id);
                continue;
            }
            if (g.ModPath is { } path && !IsSafeRelativePath(path))
            {
                rejected.Add(g.Id);
                continue;
            }
            kept.Add(g);
        }

        return new ManifestValidationResult(
            manifest with { Games = kept },
            skipped,
            rejected);
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (Path.IsPathRooted(path)) return false;          // absolute
        if (path.Contains(':')) return false;               // drive-qualified (e.g. "D:relative")
        var segments = path.Split('/', '\\');
        return !segments.Contains("..");                    // traversal
    }
}
