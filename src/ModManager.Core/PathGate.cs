namespace ModManager.Core;

/// <summary>
/// The one containment + forbidden-path + safe-relative gate shared by every site that writes
/// archive or restore-point contents into a target folder. Extracted from FrameworkInstaller and
/// DirectInject so install and restore enforce identical rules. Pure path math — no disk touch.
/// </summary>
public static class PathGate
{
    /// <summary>Normalize an archive entry to a safe relative path under the destination, or null
    /// for a directory entry or any path that escapes via traversal / absolute / drive-root.
    /// Optionally strips a single wrapper prefix (the zip's top folder).</summary>
    public static string? SafeRelative(string entryName, string? stripPrefix = null)
    {
        var n = entryName.Replace('\\', '/').TrimStart('/');
        if (n.Length == 0 || n.EndsWith("/")) return null;     // directory entry
        if (stripPrefix is not null)
        {
            var p = stripPrefix.TrimEnd('/') + "/";
            if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) n = n[p.Length..];
        }
        if (n.Length == 0) return null;
        var segs = n.Split('/');
        if (segs.Any(s => s is "" or "." or "..")) return null;  // traversal / empty segment
        if (n.Length > 1 && n[1] == ':') return null;            // drive-rooted
        return n.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>True iff <paramref name="relNorm"/> resolves to a path strictly inside
    /// <paramref name="installRootFull"/> (which MUST be fully-qualified — caller passes
    /// Path.GetFullPath(installRoot)). Rejects empty, drive-rooted, and ANY "." or ".." segment.
    /// Intentionally stricter than a bare GetFullPath check: this gate also guards the untrusted
    /// restore-replay write path, so "." segments (e.g. "Game/./x.dll") are refused outright rather
    /// than silently resolved — matching SafeRelative's segment rules.</summary>
    public static bool IsContained(string relNorm, string installRootFull)
    {
        if (string.IsNullOrEmpty(relNorm)) return false;
        var n = relNorm.Replace('\\', '/').TrimStart('/');
        if (n.Length == 0) return false;
        if (n.Split('/').Any(s => s is "" or "." or "..")) return false;
        if (n.Length > 1 && n[1] == ':') return false;
        var abs = Path.GetFullPath(Path.Combine(installRootFull, n.Replace('/', Path.DirectorySeparatorChar)));
        return abs.StartsWith(installRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(abs, installRootFull, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True iff <paramref name="relNorm"/> hits a forbidden basename OR full relative
    /// path (case-insensitive).</summary>
    public static bool IsForbidden(string relNorm, IReadOnlyList<string> forbidden)
    {
        var n = relNorm.Replace('\\', '/');
        return forbidden.Any(f =>
            string.Equals(Path.GetFileName(n), f, StringComparison.OrdinalIgnoreCase)
            || string.Equals(n, f, StringComparison.OrdinalIgnoreCase));
    }
}
