namespace ModManager.Core;

/// <summary>
/// One row from Nexus <c>updated.json</c> — a mod that changed inside the requested window.
/// <paramref name="LatestFileUpdate"/> / <paramref name="LatestModActivity"/> are unix timestamps
/// (seconds). The auto-check uses <paramref name="LatestFileUpdate"/> against a per-mod baseline to
/// narrow the per-id refresh to only the mods that actually changed.
/// </summary>
public sealed record NexusUpdateEntry(int ModId, long LatestFileUpdate, long LatestModActivity);
