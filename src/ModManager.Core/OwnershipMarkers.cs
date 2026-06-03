namespace ModManager.Core;

/// <summary>One on-disk ownership marker found in a folder: its absolute path + the tool it implies.</summary>
public sealed record OwnershipMarker(string Path, OwnerTool Owner);

/// <summary>
/// Single source of truth for the marker files that mean "another mod manager owns this folder".
/// Both <see cref="ToolOwnership.Detect"/> (read) and VortexTakeover (archive) consult
/// this so detection and takeover can never drift on what counts as a marker. Pure System.IO; no writes.
/// </summary>
public static class OwnershipMarkers
{
    // Vortex: a per-folder flag file and/or a deployment manifest. MO2: a per-mod meta.ini.
    private const string VortexFlag = "__folder_managed_by_vortex";
    private const string VortexManifestGlob = "vortex.deployment.*.json";
    private const string Mo2Meta = "meta.ini";

    /// <summary>Every ownership marker physically present in <paramref name="folderAbs"/>.</summary>
    public static IReadOnlyList<OwnershipMarker> MarkerFilesIn(string folderAbs)
    {
        var found = new List<OwnershipMarker>();
        if (string.IsNullOrWhiteSpace(folderAbs)) return found;
        try
        {
            if (!Directory.Exists(folderAbs)) return found;

            var flag = Path.Combine(folderAbs, VortexFlag);
            if (File.Exists(flag)) found.Add(new OwnershipMarker(flag, OwnerTool.Vortex));

            foreach (var m in Directory.EnumerateFiles(folderAbs, VortexManifestGlob))
                found.Add(new OwnershipMarker(m, OwnerTool.Vortex));

            var meta = Path.Combine(folderAbs, Mo2Meta);
            if (File.Exists(meta)) found.Add(new OwnershipMarker(meta, OwnerTool.Mo2));
        }
        catch { /* unreadable folder -> treat as no markers */ }
        return found;
    }

    /// <summary>The owner implied by the first marker present, or null when none.</summary>
    public static OwnerTool? OwnerOf(string folderAbs)
        => MarkerFilesIn(folderAbs) is { Count: > 0 } m ? m[0].Owner : null;
}
