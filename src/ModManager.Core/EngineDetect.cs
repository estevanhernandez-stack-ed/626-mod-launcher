namespace ModManager.Core;

/// <summary>Folder signatures the App probes for; nulls/falses mean "not found".</summary>
public sealed record EngineProbe
{
    public bool HasBepInEx { get; init; }      // BepInEx/ folder (Unity loader)
    public bool HasMelonLoader { get; init; }  // MelonLoader/ folder (Unity loader)
    public bool HasContentPaks { get; init; }  // <Game>/Content/Paks (Unreal)
    public bool HasDataPlugins { get; init; }  // Data/ with .esm/.esp/.esl (Creation Engine)
    public bool HasStardew { get; init; }       // Stardew Valley executable (SMAPI)
    public bool HasSourceAddons { get; init; }  // gameinfo.txt / addons (Source)
    public bool HasUnityData { get; init; }     // *_Data + UnityPlayer.dll, no loader yet
}

/// <summary>
/// Pure engine guessing from folder signatures. The App scans the game folder and fills an
/// <see cref="EngineProbe"/>; this picks the most specific engine, or null so the user chooses.
/// Returned keys match <see cref="EnginePresets.Presets"/>.
/// </summary>
public static class EngineDetect
{
    public static string? GuessEngine(EngineProbe p)
    {
        if (p.HasBepInEx) return "bepinex";
        if (p.HasMelonLoader) return "melonloader";
        if (p.HasContentPaks) return "ue-pak";
        if (p.HasDataPlugins) return "bethesda";
        if (p.HasStardew) return "smapi";
        if (p.HasSourceAddons) return "source";
        if (p.HasUnityData) return "bepinex"; // Unity without a loader — BepInEx is the usual route
        return null;
    }
}
