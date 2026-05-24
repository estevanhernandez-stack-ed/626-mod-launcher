namespace ModManager.Core;

/// <summary>
/// Curated Steam App ID -> engine map. The reliable signal for games whose install folder carries
/// no detectable signature — notably FromSoftware's proprietary engine (Elden Ring et al.), where
/// only the app id tells you it's a Mod Engine 2 game. Checked before folder heuristics.
/// Every value must be a real key in <see cref="EnginePresets.Presets"/> (guarded by tests).
/// </summary>
public static class KnownEngines
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        // FromSoftware (Mod Engine 2)
        ["1245620"] = "fromsoft", // Elden Ring
        ["374320"] = "fromsoft",  // Dark Souls III
        ["814380"] = "fromsoft",  // Sekiro
        ["1888160"] = "fromsoft", // Armored Core VI

        // Bethesda (Creation Engine)
        ["489830"] = "bethesda",  // Skyrim Special Edition
        ["377160"] = "bethesda",  // Fallout 4
        ["1716740"] = "bethesda", // Starfield

        // BepInEx (Unity)
        ["892970"] = "bepinex",   // Valheim
        ["1966720"] = "bepinex",  // Lethal Company

        // Unreal (.pak)
        ["990080"] = "ue-pak",    // Hogwarts Legacy
        ["1623730"] = "ue-pak",   // Palworld

        // SMAPI
        ["413150"] = "smapi",     // Stardew Valley
    };

    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var e) ? e : null;

    public static IEnumerable<string> AllMappedEngines => Map.Values.Distinct();
}
