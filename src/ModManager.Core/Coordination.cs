namespace ModManager.Core;

/// <summary>How the launcher relates to a mod location.</summary>
public enum Posture
{
    Own,        // nobody else manages it — our reversible-move model
    Conductor,  // a loader with a manifest, and unowned — we drive the manifest (no file moves)
    Coexist,    // another manager owns it — read-only, never touch
}

/// <summary>
/// Detect-and-defer arbitration. A detected runtime owner always wins (defer to it). Otherwise a
/// loader that can drive its own manifest takes the folder. A declared (profile-hint) Managed value
/// is the last, conservative fallback to Coexist — never let a stale hint block a real loader. Pure:
/// takes an already-detected owner so all IO lives in <see cref="ToolOwnership.Detect"/>.
/// </summary>
public static class Coordination
{
    public static Posture PostureFor(OwnerTool? owner, string? declaredManaged, bool loaderCanConduct)
    {
        if (owner is not null) return Posture.Coexist;
        if (loaderCanConduct) return Posture.Conductor;
        if (!string.IsNullOrEmpty(declaredManaged)) return Posture.Coexist;
        return Posture.Own;
    }
}
