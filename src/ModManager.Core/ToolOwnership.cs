namespace ModManager.Core;

/// <summary>An external tool that owns (deploys + tracks) the files in a mod folder.</summary>
public enum OwnerTool { Vortex, Mo2 }

/// <summary>
/// Detects whether another mod manager owns a folder, from on-disk markers only. Reads the
/// filesystem but holds no state and never writes. Returns null when no tool owns the folder.
/// </summary>
public static class ToolOwnership
{
    public static OwnerTool? Detect(string folderAbs)
    {
        if (string.IsNullOrWhiteSpace(folderAbs)) return null;
        try
        {
            if (!Directory.Exists(folderAbs)) return null;
            // Vortex leaves a marker file and/or a deployment manifest where it deploys.
            if (File.Exists(Path.Combine(folderAbs, "__folder_managed_by_vortex"))) return OwnerTool.Vortex;
            if (Directory.EnumerateFiles(folderAbs, "vortex.deployment.*.json").Any()) return OwnerTool.Vortex;
            // Mod Organizer 2 writes a per-mod meta.ini in folders it stages.
            if (File.Exists(Path.Combine(folderAbs, "meta.ini"))) return OwnerTool.Mo2;
            return null;
        }
        catch { return null; }
    }
}
