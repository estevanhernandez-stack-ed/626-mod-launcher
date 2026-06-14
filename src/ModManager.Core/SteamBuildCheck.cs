namespace ModManager.Core;

/// <summary>Outcome of comparing a game's recorded baseline build to its current Steam build.</summary>
public enum SteamBuildStatus
{
    /// <summary>No live build to compare (not a Steam game / appmanifest had no buildid).</summary>
    Unknown,
    /// <summary>No baseline recorded yet — the caller should set it silently (no warning).</summary>
    NoBaseline,
    /// <summary>Live build matches the baseline — nothing changed.</summary>
    Unchanged,
    /// <summary>Live build differs from the baseline — Steam updated the game; warn.</summary>
    Updated,
}

/// <summary>Pure three-way decision for the "Steam updated this game under your mods" warning.</summary>
public static class SteamBuildCheck
{
    public static SteamBuildStatus Evaluate(string? baseline, string? live)
    {
        if (string.IsNullOrEmpty(live)) return SteamBuildStatus.Unknown;
        if (string.IsNullOrEmpty(baseline)) return SteamBuildStatus.NoBaseline;
        return baseline == live ? SteamBuildStatus.Unchanged : SteamBuildStatus.Updated;
    }
}
