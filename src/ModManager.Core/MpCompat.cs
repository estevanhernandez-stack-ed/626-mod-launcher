namespace ModManager.Core;

/// <summary>
/// Multiplayer-safety verdict for a mod. Unknown means "no claim" — we don't pretend to know.
/// </summary>
public enum MpRisk { Unknown, Safe, Risky, SpOnly }

/// <summary>
/// MP-safety core: infer a mod's multiplayer risk from its class/kind, and resolve the
/// state to show when the user has set an explicit override. A real user override always wins;
/// an absent or Unknown override falls back to what we inferred. Pure — no filesystem, no UI.
/// </summary>
public static class MpCompat
{
    /// <summary>
    /// Infer MP risk from a mod's class/kind string. Case-insensitive. An unrecognized class
    /// (including null / empty / "both" / "dll" / "tweak" / "sp" / "mp") yields Unknown — we make no claim.
    /// </summary>
    public static MpRisk Infer(string? modClass)
    {
        if (string.IsNullOrWhiteSpace(modClass)) return MpRisk.Unknown;

        return modClass.Trim().ToLowerInvariant() switch
        {
            "graphics" or "display" or "upscaler" or "co-op" => MpRisk.Safe,
            "gameplay" => MpRisk.Risky,
            _ => MpRisk.Unknown,
        };
    }

    /// <summary>
    /// The state to show: a real user override (Safe / Risky / SpOnly) wins; a null override
    /// or an Unknown override means "Auto" — fall back to the inferred risk.
    /// </summary>
    public static MpRisk Effective(MpRisk inferred, MpRisk? userOverride)
        => userOverride is { } o && o != MpRisk.Unknown ? o : inferred;
}
