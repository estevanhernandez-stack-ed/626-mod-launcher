namespace ModManager.Core.RestorePoints;

/// <summary>A restore-point game whose id matches a live game but points at a DIFFERENT GameRoot.
/// Restore must surface this (App dialog) rather than overwriting — the data-dir path is derived
/// from id+GameRoot, so a blind upsert would point _626mods at the wrong place.</summary>
public sealed record RestoreConflict(string Id, string ManifestGameRoot, string LiveGameRoot);

public static class RestoreReconcile
{
    /// <summary>Pure: returns the id/GameRoot conflicts. Writes nothing. A same-id-same-root or a
    /// brand-new id is NOT a conflict (those upsert cleanly).</summary>
    public static IReadOnlyList<RestoreConflict> Check(
        RestorePointManifest m, IReadOnlyList<GameEntry> live)
    {
        var byId = live.ToDictionary(g => g.Id, g => g.GameRoot, StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<RestoreConflict>();
        foreach (var ga in m.Games)
        {
            if (byId.TryGetValue(ga.Id, out var liveRoot)
                && !string.Equals(NormRoot(liveRoot), NormRoot(ga.GameRoot), StringComparison.OrdinalIgnoreCase))
            {
                conflicts.Add(new RestoreConflict(ga.Id, ga.GameRoot, liveRoot));
            }
        }
        return conflicts;
    }

    private static string NormRoot(string p)
        => string.IsNullOrEmpty(p) ? p : Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
}
