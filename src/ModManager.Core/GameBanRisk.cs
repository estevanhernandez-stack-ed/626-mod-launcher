namespace ModManager.Core;

/// <summary>A game's anti-cheat/ban exposure for online modding. Ordered so a numeric max works.</summary>
public enum GameBanRisk { None = 0, Low = 1, Medium = 2, High = 3 }

/// <summary>
/// Pure rules for the game-level ban-risk flag: parse the descriptive manifest string, the
/// never-downgrade merge, and the single enable-gate decision every enable path consults. No IO,
/// no UI. The manifest flag is descriptive ("this game is risky"); these rules are the compiled
/// policy that decides what to do about it.
/// </summary>
public static class BanRiskRules
{
    /// <summary>Map the manifest string to a level. null / unknown / garbage -> None, case-insensitive.</summary>
    public static GameBanRisk Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "low" => GameBanRisk.Low,
        "medium" => GameBanRisk.Medium,
        "high" => GameBanRisk.High,
        _ => GameBanRisk.None,
    };

    /// <summary>The canonical manifest string for a level, or null for None.</summary>
    public static string? Canonical(GameBanRisk r) => r switch
    {
        GameBanRisk.Low => "low",
        GameBanRisk.Medium => "medium",
        GameBanRisk.High => "high",
        _ => null,
    };

    /// <summary>The higher of two levels.</summary>
    public static GameBanRisk Max(GameBanRisk a, GameBanRisk b) => (GameBanRisk)System.Math.Max((int)a, (int)b);

    /// <summary>Merge two manifest strings, NEVER downgrading: the higher level's canonical string wins.
    /// A safety field must not be silently lowered by a remote feed (mirrors the Provenance never-downgrade rule).</summary>
    public static string? MaxString(string? a, string? b) => Canonical(Max(Parse(a), Parse(b)));

    /// <summary>High risk gates an enable until the user has acknowledged it for this game.
    /// Medium/Low/None never gate (banner-only). Single source of truth for every enable path.</summary>
    public static bool ShouldGateEnable(GameBanRisk level, bool alreadyAcked)
        => level == GameBanRisk.High && !alreadyAcked;
}
