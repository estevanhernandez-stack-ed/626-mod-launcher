using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Installs save/world mods into the user's SAVE TREE. SAFETY-CRITICAL — this writes inside the
/// folder the game itself owns, so three invariants are load-bearing and enforced on every op:
///
///   1. NEVER write under a game-managed folder (RocksDB_v2 / RocksDB_v2_Backups). Resolving a
///      target whose path contains a forbidden segment is a hard refusal (InvalidOperationException).
///   2. SNAPSHOT FIRST. Every mutating op (install / reset / remove) backs up the whole save tree
///      via <see cref="SaveManager.Backup"/> BEFORE it deletes or extracts anything — law #3,
///      reversible by default.
///   3. ZIP-SLIP GUARD. Every extracted entry is reduced to a relative path under the target
///      &lt;guid&gt; folder; anything that would escape (traversal, absolute, drive-rooted) is refused.
///
/// Pure System.IO — no Electron, no UI. Orchestrates over <see cref="SaveManager"/>.
/// </summary>
public static partial class SaveModInstaller
{
    /// <summary>Game-managed save subfolders the app must never write under — enforced on top of any
    /// profile-declared forbidden list.</summary>
    public static readonly IReadOnlyList<string> DefaultForbidden = new[] { "RocksDB_v2", "RocksDB_v2_Backups" };

    private const string DefaultSaveModPath = "RocksDB/{version}/Worlds";
    private const string VersionToken = "{version}";

    /// <summary>
    /// The absolute Worlds target dir under the single profile folder, with {version} resolved by
    /// scanning the on-disk RocksDB\ for a version subdir. A null <paramref name="saveModPath"/>
    /// uses the built-in default "RocksDB/{version}/Worlds". THROWS InvalidOperationException if the
    /// resolved path contains any forbidden segment (profile list ∪ DefaultForbidden, case-insensitive,
    /// matched as a whole path segment) — and refuses BEFORE creating anything. Creates the Worlds
    /// dir if missing. Clear errors for zero/multiple profiles and no RocksDB version.
    /// </summary>
    public static string ResolveWorldsTarget(string saveProfilesDir, string? saveModPath, IReadOnlyList<string>? forbidden,
                                             bool create = true)
    {
        var profile = SingleProfileDir(saveProfilesDir);

        var relTemplate = string.IsNullOrWhiteSpace(saveModPath) ? DefaultSaveModPath : saveModPath!;

        // Guard the TEMPLATE segments first — a forbidden literal (e.g. "RocksDB_v2") must refuse
        // before we touch the disk or create any directory. {version} is not yet substituted, so
        // the literal forbidden name is visible here.
        var forbidSet = BuildForbiddenSet(forbidden);
        GuardSegments(SplitTemplate(relTemplate), forbidSet);

        var version = ResolveVersion(profile, relTemplate);
        var relResolved = relTemplate.Replace(VersionToken, version, StringComparison.OrdinalIgnoreCase);

        var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(profile, NormalizeRel(relResolved)));

        // Defense-in-depth: guard the fully-resolved absolute path's segments too.
        GuardSegments(target.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar), forbidSet);

        if (create) Directory.CreateDirectory(target);   // a lookup (WorldDirFor) never writes the save tree
        return target;
    }

    /// <summary>
    /// Install a world zip: (1) resolve the (forbidden-guarded) Worlds target, (2) refuse a world that is
    /// already there, (3) snapshot the save tree FIRST, (4) extract the zip's &lt;guid&gt; world into
    /// &lt;target&gt;\&lt;guid&gt;\ with a zip-slip guard, (5) keep a copy of the zip at
    /// <see cref="KeptZipPath"/> for reset. Returns the installed Worlds\&lt;guid&gt; path.
    /// </summary>
    public static string InstallWorld(string saveProfilesDir, string snapshotsDir, string saveModStoreDir,
                                      string zipPath, string worldGuid, string? saveModPath, IReadOnlyList<string>? forbidden)
    {
        RequireSafeGuid(worldGuid); // refuse a traversal worldGuid BEFORE touching the save tree
        var target = ResolveWorldsTarget(saveProfilesDir, saveModPath, forbidden); // guarded
        var worldDir = SafeWorldDir(target, worldGuid);

        // Installing over a world that is already there extracted until the first file that existed and then
        // threw, leaving a mix of the two. Refused before the snapshot, so nothing is written.
        if (Directory.Exists(worldDir) && Directory.EnumerateFileSystemEntries(worldDir).Any())
            throw new WorldAlreadyPresentException(worldGuid, worldDir);

        // Keep a copy of the zip for reset, in a folder of this world's own (the download can be deleted, and two
        // worlds' zips can share a file name), BEFORE the world goes in: a copy that fails afterwards would leave a
        // world with no record, which the already-present check would then refuse to reinstall over.
        var kept = KeptZipPath(saveModStoreDir, worldGuid, zipPath);
        var copied = !string.Equals(System.IO.Path.GetFullPath(kept), System.IO.Path.GetFullPath(zipPath), StringComparison.OrdinalIgnoreCase)
                     && !File.Exists(kept);   // a copy an earlier install of this world kept is not this install's to take back
        if (!string.Equals(System.IO.Path.GetFullPath(kept), System.IO.Path.GetFullPath(zipPath), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(kept)!);
            File.Copy(zipPath, kept, overwrite: true);
        }

        var worldExisted = Directory.Exists(worldDir);   // empty, by the check above
        try
        {
            SaveManager.Backup(saveProfilesDir, snapshotsDir, "before-savemod", auto: true); // snapshot FIRST
            Directory.CreateDirectory(worldDir);
            ExtractWorld(zipPath, worldGuid, worldDir, overwrite: false);
        }
        catch
        {
            // Nothing was there before, so take back what this install put in, the kept copy included: a half
            // world with no record is the state the already-present check can't get out of.
            try { if (!worldExisted && Directory.Exists(worldDir)) LinkSafeDelete.DeleteTree(worldDir); } catch { }
            try { if (copied) File.Delete(kept); } catch { }
            throw;
        }

        return worldDir;
    }

    /// <summary>The folder world <paramref name="worldGuid"/> lives in under the (forbidden-guarded) Worlds
    /// target, whether or not it is there now. Writes nothing, so a preview can call it. Throws as
    /// <see cref="ResolveWorldsTarget"/> does for a missing or ambiguous profile, or an unsafe id.</summary>
    public static string WorldDirFor(string saveProfilesDir, string? saveModPath, IReadOnlyList<string>? forbidden, string worldGuid)
    {
        RequireSafeGuid(worldGuid);
        return SafeWorldDir(ResolveWorldsTarget(saveProfilesDir, saveModPath, forbidden, create: false), worldGuid);
    }

    /// <summary>Where <see cref="InstallWorld"/> keeps a world's zip for reset:
    /// <c>&lt;store&gt;\save-mods\&lt;guid&gt;\&lt;zip file name&gt;</c>. The record points here, not at the
    /// download.</summary>
    public static string KeptZipPath(string saveModStoreDir, string worldGuid, string zipPath)
    {
        RequireSafeGuid(worldGuid);
        return System.IO.Path.Combine(saveModStoreDir, "save-mods", worldGuid, System.IO.Path.GetFileName(zipPath));
    }

    /// <summary>True when the zip at <paramref name="zipPath"/> carries the world <paramref name="worldGuid"/> (a
    /// folder of that name anywhere in it). False for an unreadable zip.</summary>
    public static bool ZipHoldsWorld(string zipPath, string worldGuid)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Any(e => e.FullName.Replace('\\', '/').Split('/')
                .Any(seg => string.Equals(seg, worldGuid, StringComparison.OrdinalIgnoreCase)));
        }
        catch { return false; }
    }

    /// <summary>Reset: resolve (guarded) -&gt; snapshot first -&gt; delete &lt;target&gt;\&lt;guid&gt;
    /// -&gt; re-extract the kept zip (overwrite).</summary>
    public static void ResetWorld(string saveProfilesDir, string snapshotsDir, string keptZipPath,
                                  string worldGuid, string? saveModPath, IReadOnlyList<string>? forbidden)
    {
        RequireSafeGuid(worldGuid); // refuse a traversal worldGuid BEFORE touching the save tree
        var target = ResolveWorldsTarget(saveProfilesDir, saveModPath, forbidden); // guarded
        SaveManager.Backup(saveProfilesDir, snapshotsDir, "before-savemod-reset", auto: true); // snapshot FIRST

        var worldDir = SafeWorldDir(target, worldGuid);
        if (Directory.Exists(worldDir)) Directory.Delete(worldDir, recursive: true);
        Directory.CreateDirectory(worldDir);
        ExtractWorld(keptZipPath, worldGuid, worldDir, overwrite: true);
    }

    /// <summary>Remove: resolve (guarded) -&gt; snapshot first -&gt; delete &lt;target&gt;\&lt;guid&gt;.</summary>
    public static void RemoveWorld(string saveProfilesDir, string snapshotsDir, string worldGuid,
                                   string? saveModPath, IReadOnlyList<string>? forbidden)
    {
        RequireSafeGuid(worldGuid); // refuse a traversal worldGuid BEFORE touching the save tree
        var target = ResolveWorldsTarget(saveProfilesDir, saveModPath, forbidden); // guarded
        SaveManager.Backup(saveProfilesDir, snapshotsDir, "before-savemod-remove", auto: true); // snapshot FIRST

        var worldDir = SafeWorldDir(target, worldGuid);
        if (Directory.Exists(worldDir)) Directory.Delete(worldDir, recursive: true);
    }

    // ---------------- worldGuid guard ----------------

    // A world id is a GUID — 32 hex, or the dashed form. It comes from a dropped zip (attacker-
    // influenced) and becomes a directory NAME under the save tree, so it must be a single safe
    // segment. This is what stops a worldGuid like "..\..\RocksDB_v2" from escaping Worlds and
    // deleting/writing a game-managed folder (the headline save-tree exploit).
    [GeneratedRegex(@"^[0-9A-Fa-f]{32}$|^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$")]
    private static partial Regex GuidRe();

    private static void RequireSafeGuid(string worldGuid)
    {
        if (string.IsNullOrWhiteSpace(worldGuid)
            || worldGuid != System.IO.Path.GetFileName(worldGuid) // no separators / path parts
            || !GuidRe().IsMatch(worldGuid))
            throw new InvalidOperationException($"Unsafe world id \"{worldGuid}\" — refusing to touch the save tree.");
    }

    // Combine the (validated) guid under target and re-assert containment — defense in depth.
    private static string SafeWorldDir(string target, string worldGuid)
    {
        RequireSafeGuid(worldGuid);
        var worldDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(target, worldGuid));
        if (!IsUnder(target, worldDir))
            throw new InvalidOperationException($"Unsafe world id \"{worldGuid}\" — resolves outside the Worlds folder.");
        return worldDir;
    }

    // ---------------- forbidden guard ----------------

    private static HashSet<string> BuildForbiddenSet(IReadOnlyList<string>? forbidden)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in forbidden ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(f)) set.Add(f.Trim());
        foreach (var f in DefaultForbidden) set.Add(f);
        return set;
    }

    // Refuse if any path segment case-insensitively equals a forbidden name. The {version} token
    // is left intact so it never accidentally matches a forbidden literal.
    private static void GuardSegments(IEnumerable<string> segments, HashSet<string> forbidden)
    {
        foreach (var seg in segments)
        {
            if (string.IsNullOrEmpty(seg)) continue;
            if (forbidden.Contains(seg))
                throw new InvalidOperationException(
                    $"Refusing to write under the game-managed save folder \"{seg}\" — that path is off-limits.");
        }
    }

    private static IEnumerable<string> SplitTemplate(string rel)
        => rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeRel(string rel)
        => rel.Replace('\\', '/').Replace('/', System.IO.Path.DirectorySeparatorChar);

    // ---------------- profile + version resolution ----------------

    private static string SingleProfileDir(string saveProfilesDir)
    {
        if (!Directory.Exists(saveProfilesDir))
            throw new InvalidOperationException("No save profile found — open the game once.");
        var dirs = Directory.GetDirectories(saveProfilesDir);
        if (dirs.Length == 0) throw new InvalidOperationException("No save profile found — open the game once.");
        if (dirs.Length > 1) throw new InvalidOperationException("Multiple save profiles found — not yet supported.");
        return dirs[0];
    }

    // The RocksDB version subdir to substitute for {version}: highest by System.Version, else
    // ordinal-desc. Throws if there is no RocksDB\ or it has no subdir.
    private static string ResolveVersion(string profile, string relTemplate)
    {
        // The {version} token lives directly under the segment before it — for the built-in path
        // and Windrose that segment is "RocksDB". Find it from the template, default "RocksDB".
        var rocksRel = RocksDbRelBeforeVersion(relTemplate);
        var rocksDir = System.IO.Path.Combine(profile, rocksRel);
        if (!Directory.Exists(rocksDir))
            throw new InvalidOperationException("Could not find a RocksDB save version — open the game once.");

        var versions = Directory.GetDirectories(rocksDir).Select(d => new DirectoryInfo(d).Name).ToList();
        if (versions.Count == 0)
            throw new InvalidOperationException("Could not find a RocksDB save version — open the game once.");

        return versions
            .OrderByDescending(v => System.Version.TryParse(v, out var parsed) ? parsed : new System.Version(0, 0))
            .ThenByDescending(v => v, StringComparer.Ordinal)
            .First();
    }

    // The relative path up to (not including) the {version} segment, joined with the OS separator.
    // For "RocksDB/{version}/Worlds" -> "RocksDB". If there's no token, default to "RocksDB".
    private static string RocksDbRelBeforeVersion(string relTemplate)
    {
        var segs = relTemplate.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var idx = Array.FindIndex(segs, s => string.Equals(s, VersionToken, StringComparison.OrdinalIgnoreCase));
        if (idx <= 0) return "RocksDB";
        return string.Join(System.IO.Path.DirectorySeparatorChar, segs.Take(idx));
    }

    // ---------------- zip-slip-safe extraction ----------------

    // Extract a world zip into <worldDir>. Each file entry is reduced to its path relative to the
    // <guid>/ segment (everything after it); if the zip has no <guid>/ segment, the whole entry
    // path is the relative path. Any rel path that escapes <worldDir> (traversal / absolute /
    // drive-rooted) is refused — safe siblings still install.
    private static void ExtractWorld(string zipPath, string worldGuid, string worldDir, bool overwrite)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var rel = RelUnderGuid(entry.FullName, worldGuid);
            if (rel is null) continue; // unsafe (traversal/absolute/drive) — refuse this entry

            var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(worldDir, rel));
            if (!IsUnder(worldDir, dest)) continue; // belt-and-suspenders escape check

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite);
        }
    }

    // The entry's path relative to the <guid>/ folder (everything after the first <guid>/ segment),
    // or the whole path if no <guid>/ segment is present. Returns null for any unsafe rel path.
    //
    // Distinct from PathGate.SafeRelative — kept separate intentionally.
    // PathGate.SafeRelative strips an optional wrapper prefix from the START of the path. This
    // method locates the <guid> segment anywhere in the entry path (zip layouts vary: "guid/file",
    // "worlds/guid/file", etc.) and strips everything up to and including it. That mid-path search
    // is not expressible via PathGate.SafeRelative's stripPrefix parameter, so forcing the delegation
    // would change behavior. The segment/traversal/drive-root rules it applies are identical to
    // PathGate.SafeRelative's — this is just a different extraction contract.
    private static string? RelUnderGuid(string entryName, string worldGuid)
    {
        var n = entryName.Replace('\\', '/').TrimStart('/');
        if (n.Length == 0 || n.EndsWith("/")) return null;

        var segs = n.Split('/');
        var guidIdx = Array.FindIndex(segs, s => string.Equals(s, worldGuid, StringComparison.OrdinalIgnoreCase));
        var relSegs = guidIdx >= 0 ? segs.Skip(guidIdx + 1).ToArray() : segs;
        if (relSegs.Length == 0) return null;

        if (relSegs.Any(s => s is "" or "." or "..")) return null;     // traversal / empty segment
        var rel = string.Join('/', relSegs);
        if (rel.Length > 1 && rel[1] == ':') return null;              // drive-rooted

        return rel.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }

    // True if <path> resolves to a location strictly inside <root>.
    // Delegates to PathGate.IsContainedAbsolute — the canonical absolute-path containment gate.
    // (DirectInject.IsUnder was the previous reference; it now delegates to PathGate too.)
    private static bool IsUnder(string root, string path)
        => PathGate.IsContainedAbsolute(path, root);
}

/// <summary>A world with this id is already in the save folder. Installing over it would mix two versions,
/// so it is refused before anything is written. Whether 626 installed it (and so can reset or remove it) is
/// the caller's to say: this only knows the folder is there.</summary>
public sealed class WorldAlreadyPresentException(string worldGuid, string worldDir)
    : InvalidOperationException($"World {worldGuid} is already in the save folder ({worldDir}). Nothing was changed.")
{
    public string WorldGuid { get; } = worldGuid;
    public string WorldDir { get; } = worldDir;
}
