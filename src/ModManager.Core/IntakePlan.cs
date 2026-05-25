namespace ModManager.Core;

/// <summary>A new (non-colliding) file in a drop. IncomingSource is a loose path or "zipPath!entryName".
/// RelPath is the destination key (flat name for mod-folder games; nested for direct-inject).</summary>
public sealed record IntakeItem(string Name, string RelPath, string IncomingSource);

/// <summary>A file in a drop whose destination already exists — needs a replace/skip decision.
/// RelPath is the identity key; IncomingSource is a loose path or "zipPath!entryName".</summary>
public sealed record IntakeCollision(string Name, string RelPath, string ExistingPath, string IncomingSource);

/// <summary>The result of planning a drop without touching disk: what is new, what collides, what is refused.</summary>
public sealed record IntakePlan(
    IReadOnlyList<IntakeItem> ToAdd,
    IReadOnlyList<IntakeCollision> Collisions,
    IReadOnlyList<SkippedItem> Unsafe);
