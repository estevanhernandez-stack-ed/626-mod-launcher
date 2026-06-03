namespace ModManager.Core;

/// <summary>An external tool that owns (deploys + tracks) the files in a mod folder.</summary>
public enum OwnerTool { Vortex, Mo2 }

/// <summary>How the launcher reads a folder's ownership, given the taken-over set.</summary>
public enum OwnershipState
{
    NotOwned,    // ours to manage (no marker, or taken over and marker archived)
    Owned,       // another manager owns it (marker present, not taken over)
    ReDeployed,  // taken over BUT a marker reappeared — the other manager re-deployed
}

/// <summary>The resolved ownership of a folder.</summary>
public sealed record OwnershipResolution(OwnershipState State, OwnerTool? Owner);

/// <summary>
/// Reads whether another mod manager owns a folder, from on-disk markers only (via OwnershipMarkers).
/// <see cref="Detect"/> is the raw marker read; <see cref="Resolve"/> layers in the launcher's
/// taken-over set so a folder the user took over reads as ours (and a reappeared marker reads as
/// re-deployed). Reads the filesystem but holds no state and never writes.
/// </summary>
public static class ToolOwnership
{
    public static OwnerTool? Detect(string folderAbs) => OwnershipMarkers.OwnerOf(folderAbs);

    /// <summary>Resolve a folder's ownership against the taken-over set. A taken-over folder reads as
    /// NotOwned even with a marker still present — UNLESS a fresh marker reappeared, which reads as
    /// ReDeployed so the App can surface a "re-deployed" notice.</summary>
    /// <param name="takenOver">The persisted taken-over folders. Membership uses THIS set's own
    /// comparer — pass a case-insensitive set (StringComparer.OrdinalIgnoreCase) for Windows paths,
    /// as TakenOverStore.Load does.</param>
    public static OwnershipResolution Resolve(string folderAbs, IReadOnlySet<string> takenOver)
    {
        var owner = OwnershipMarkers.OwnerOf(folderAbs);
        var isTakenOver = takenOver.Contains(folderAbs);

        if (owner is null) return new OwnershipResolution(OwnershipState.NotOwned, null);   // no marker
        if (isTakenOver) return new OwnershipResolution(OwnershipState.ReDeployed, owner);  // marker came back
        return new OwnershipResolution(OwnershipState.Owned, owner);                        // owned as today
    }
}
