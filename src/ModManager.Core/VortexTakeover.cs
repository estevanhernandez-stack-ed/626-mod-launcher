using System.Text.Json;

namespace ModManager.Core;

/// <summary>The persisted set of folders the user has taken over from another manager. Lives at
/// <c>&lt;dataDir&gt;/taken-over.json</c> (camelCase). Posture consults it so a taken-over folder reads
/// as not-owned even if a marker is still physically present.</summary>
public sealed class TakenOverState
{
    public int Version { get; set; } = 1;
    public List<string> Folders { get; set; } = new();
}

/// <summary>Read/write the taken-over set. camelCase via AtomicJson; case-insensitive on path.</summary>
public static class TakenOverStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "taken-over.json");

    /// <summary>The taken-over folders as a case-insensitive set. Missing/corrupt file -> empty.</summary>
    public static HashSet<string> Load(string dataDir)
    {
        try
        {
            var p = PathFor(dataDir);
            if (!File.Exists(p)) return new(StringComparer.OrdinalIgnoreCase);
            var state = JsonSerializer.Deserialize<TakenOverState>(File.ReadAllText(p), Json);
            return new HashSet<string>(state?.Folders ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void Add(string dataDir, string folderAbs)
    {
        var set = Load(dataDir);
        if (!set.Add(Path.GetFullPath(folderAbs))) return;   // normalize so set membership is separator/relative-stable
        Save(dataDir, set);
    }

    public static void Remove(string dataDir, string folderAbs)
    {
        var set = Load(dataDir);
        if (!set.Remove(Path.GetFullPath(folderAbs))) return;
        Save(dataDir, set);
    }

    private static void Save(string dataDir, HashSet<string> set)
        => AtomicJson.WriteJsonAtomic(PathFor(dataDir), new TakenOverState { Version = 1, Folders = set.ToList() });
}

/// <summary>One archived marker in a takeover manifest: where it came from + the file we stored.</summary>
public sealed record ArchivedMarker(string OriginalPath, string ArchivedName, string Owner);

/// <summary>The manifest written into a takeover archive dir, recording how to reverse the takeover.</summary>
public sealed class TakeoverManifest
{
    public int Version { get; set; } = 1;
    public DateTime TakenOverUtc { get; set; }
    public List<ArchivedMarker> Markers { get; set; } = new();
}

/// <summary>The result of a TakeOver call.</summary>
public sealed record TakeoverResult(bool Success, string FolderAbs, IReadOnlyList<ArchivedMarker> ArchivedMarkers, string? Error = null);

/// <summary>
/// Reversible takeover of a folder owned by another manager (Vortex / MO2). Archives every ownership
/// marker out of the folder into <c>&lt;dataDir&gt;/vortex-takeover/&lt;locationKey&gt;/</c> (move, never
/// delete), records the folder in the taken-over set, and writes a manifest so Undo can restore the
/// markers byte-for-byte. Stage-then-commit: a mid-move failure rolls back, leaving the folder owned.
/// Pure System.IO + System.Text.Json. Game-scoped by caller (operates on one folder).
/// </summary>
public static partial class VortexTakeover
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Stable, collision-free archive key for a folder: a human-readable slug of its path
    /// relative to the game root, plus a short hash of that relative path so two folders that slug to
    /// the same string (e.g. "R5/Mods" vs a folder named "R5_Mods") never share an archive dir.</summary>
    public static string LocationKey(string gameRoot, string folderAbs)
    {
        var rel = Path.GetRelativePath(gameRoot, folderAbs);
        var slug = rel.Replace(Path.DirectorySeparatorChar, '_').Replace('/', '_').Replace(':', '_');
        if (string.IsNullOrWhiteSpace(slug) || slug == ".") slug = "_root";
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(rel)))[..8].ToLowerInvariant();
        return slug + "-" + hash;
    }

    public static TakeoverResult TakeOver(string dataDir, string gameRoot, string folderAbs)
    {
        var markers = OwnershipMarkers.MarkerFilesIn(folderAbs);
        if (markers.Count == 0)
            return new TakeoverResult(true, folderAbs, Array.Empty<ArchivedMarker>()); // already ours

        var archiveDir = Path.Combine(dataDir, "vortex-takeover", LocationKey(gameRoot, folderAbs));
        Directory.CreateDirectory(archiveDir);

        var moved = new List<(string from, string to)>();
        var archived = new List<ArchivedMarker>();
        try
        {
            foreach (var m in markers)
            {
                var name = Path.GetFileName(m.Path);
                var dest = Path.Combine(archiveDir, name);
                File.Move(m.Path, dest, overwrite: true);  // move-to-holding (reversible)
                moved.Add((m.Path, dest));
                archived.Add(new ArchivedMarker(m.Path, name, m.Owner.ToString().ToLowerInvariant()));
            }

            // The manifest write is inside the rollback region: if it fails (disk full, etc.) we must
            // restore the markers too, or they're stranded — archived out of the folder with no manifest
            // for Undo to find. A manifest-write failure therefore rolls back the moves like any move failure.
            AtomicJson.WriteJsonAtomic(Path.Combine(archiveDir, "takeover.json"),
                new TakeoverManifest { Version = 1, TakenOverUtc = DateTime.UtcNow, Markers = archived });
        }
        catch (Exception ex)
        {
            // Roll back any markers already moved, leave the folder owned.
            foreach (var (from, to) in moved)
                try { File.Move(to, from, overwrite: true); } catch { /* best-effort */ }
            return new TakeoverResult(false, folderAbs, Array.Empty<ArchivedMarker>(), ex.Message);
        }

        // Commit-of-record: only reached when the whole try (moves + manifest) succeeded.
        TakenOverStore.Add(dataDir, folderAbs);
        return new TakeoverResult(true, folderAbs, archived);
    }

    /// <summary>Take over every passed location. The caller passes ONLY the active game's Vortex-owned
    /// locations (e.g. from ctx.Locations) — this method never discovers folders on its own, so it is
    /// intrinsically game-scoped and can never touch another game's folders.</summary>
    public static IReadOnlyList<TakeoverResult> TakeOverGame(
        string dataDir, string gameRoot, IReadOnlyList<string> ownedLocationAbsPaths)
        => (ownedLocationAbsPaths ?? Array.Empty<string>())
            .Select(folder => TakeOver(dataDir, gameRoot, folder))
            .ToList();

    public static void Undo(string dataDir, string folderAbs)
    {
        // Find the archive dir by scanning vortex-takeover/* for a manifest whose markers point back into
        // folderAbs (robust to not having the gameRoot here). Restore each marker, then remove the record.
        var root = Path.Combine(dataDir, "vortex-takeover");
        if (Directory.Exists(root))
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var manifestPath = Path.Combine(dir, "takeover.json");
                if (!File.Exists(manifestPath)) continue;
                TakeoverManifest? man;
                try { man = JsonSerializer.Deserialize<TakeoverManifest>(File.ReadAllText(manifestPath), Json); }
                catch { continue; }
                if (man is null) continue;
                if (!man.Markers.Any(mk => string.Equals(Path.GetDirectoryName(mk.OriginalPath), folderAbs, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var allRestored = true;
                foreach (var mk in man.Markers)
                {
                    var archivedPath = Path.Combine(dir, mk.ArchivedName);
                    try
                    {
                        if (File.Exists(archivedPath))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(mk.OriginalPath)!);
                            File.Move(archivedPath, mk.OriginalPath, overwrite: true);
                        }
                    }
                    catch { allRestored = false; /* degrade: restore what we can, keep the archive */ }
                }
                // Only tear down the archive when EVERY marker made it home — the archive is the rollback
                // surface, so a partial restore (e.g. a marker file locked by a running game) must keep it
                // for a later retry rather than destroy the last copy.
                if (allRestored)
                    try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
                break;
            }
        }
        TakenOverStore.Remove(dataDir, folderAbs);
    }
}
