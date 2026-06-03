namespace ModManager.Core;

/// <summary>An external tool that owns (deploys + tracks) the files in a mod folder.</summary>
public enum OwnerTool { Vortex, Mo2 }

/// <summary>
/// Detects whether another mod manager owns a folder, from on-disk markers only. Reads the
/// filesystem but holds no state and never writes. Returns null when no tool owns the folder.
/// </summary>
public static class ToolOwnership
{
    public static OwnerTool? Detect(string folderAbs) => OwnershipMarkers.OwnerOf(folderAbs);
}
