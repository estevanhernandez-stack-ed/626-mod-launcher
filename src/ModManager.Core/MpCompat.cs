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

    // Explicit-claim phrase tables. Verifying MP-safety is the user's job (test it); the one place
    // we promote past "MP?" without guessing is when the author states it outright. Checked in
    // precedence order so a negative claim ("single player only") always beats a positive one.
    private static readonly string[] SpOnlyPhrases =
    {
        "single player only", "singleplayer only", "single-player only", "sp only", "sp-only",
        "multiplayer not supported", "not multiplayer compatible", "no multiplayer support",
        "not work in multiplayer", "won't work in multiplayer", "will not work in multiplayer",
    };
    private static readonly string[] RiskyPhrases =
    {
        "get you banned", "get banned", "may get banned", "will get banned",
        "anti-cheat", "anticheat", "easy anti-cheat",
        "do not use in multiplayer", "don't use in multiplayer", "do not use online",
    };
    private static readonly string[] SafePhrases =
    {
        "multiplayer safe", "mp safe", "safe for multiplayer", "safe in multiplayer",
        "works in multiplayer", "works in mp", "multiplayer compatible", "mp compatible",
        "co-op compatible", "coop compatible", "works in co-op", "works in coop",
        "server-side", "server side", "client-side safe",
    };

    /// <summary>
    /// Infer MP risk from an author's readme/description by scanning for EXPLICIT statements only.
    /// No keyword guessing about gameplay — only the author saying so. Silence yields Unknown.
    /// Negative claims (SpOnly) outrank ban/anti-cheat warnings, which outrank safe claims.
    /// </summary>
    public static MpRisk InferFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return MpRisk.Unknown;
        var t = text.ToLowerInvariant();
        if (SpOnlyPhrases.Any(p => t.Contains(p))) return MpRisk.SpOnly;
        if (RiskyPhrases.Any(p => t.Contains(p))) return MpRisk.Risky;
        if (SafePhrases.Any(p => t.Contains(p))) return MpRisk.Safe;
        return MpRisk.Unknown;
    }

    /// <summary>
    /// Combined non-override inference: an explicit readme/description claim outranks the class hint
    /// ("unless the readme says otherwise"); class is only a fallback when the text makes no claim.
    /// </summary>
    public static MpRisk InferAll(string? modClass, string? text)
    {
        var fromText = InferFromText(text);
        return fromText != MpRisk.Unknown ? fromText : Infer(modClass);
    }

    /// <summary>
    /// The state to show: a real user override (Safe / Risky / SpOnly) wins; a null override
    /// or an Unknown override means "Auto" — fall back to the inferred risk.
    /// </summary>
    public static MpRisk Effective(MpRisk inferred, MpRisk? userOverride)
        => userOverride is { } o && o != MpRisk.Unknown ? o : inferred;
}
