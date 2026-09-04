namespace ModManager.Core;

/// <summary>One installed Steam game the importer can plan an auto-add for. Mirrors the store-
/// agnostic <see cref="InstalledGame"/> (appId + display name + resolved install folder) but stays
/// in Core so the planning logic is headless-testable.</summary>
public sealed record SteamImportCandidate(string AppId, string Name, string GameRoot);

/// <summary>The auto-add plan for one Steam game. <see cref="Addable"/> is true when the engine
/// resolved and <see cref="Input"/> is ready to register; false means the engine couldn't be
/// detected and the caller should route the game to the manual wizard rather than register a guess.</summary>
public sealed record SteamImportPlan(string Name, string AppId, bool Addable, string? Engine, GameInput? Input);

/// <summary>
/// Pure planner for "add from Steam" auto-add. Turns an installed Steam game into a ready-to-register
/// <see cref="GameInput"/> — resolving the engine, defaulting the mod path from the engine preset, and
/// carrying the Steam app id through. Engine resolution priority: the app-id map first (most reliable —
/// it catches proprietary engines like FromSoft's that leave no folder signature), then the folder
/// scan the caller supplies. When neither resolves, the game is flagged not-addable so the UI can send
/// it to the manual flow instead of registering a wrong/custom engine.
/// </summary>
public static class SteamGameImport
{
    /// <param name="folderDetectedEngine">The engine from a folder scan (App calls
    /// <see cref="EngineScan.Detect"/>), or null. Used only when the app-id map misses.</param>
    public static SteamImportPlan Plan(SteamImportCandidate game, string? folderDetectedEngine)
    {
        var engine = KnownEngines.ByAppId(game.AppId) ?? folderDetectedEngine;
        if (string.IsNullOrEmpty(engine))
            return new SteamImportPlan(game.Name, game.AppId, Addable: false, Engine: null, Input: null);

        // The manifest's per-game modPath is an OVERRIDE to the engine default (GameManifest.ModPath
        // says so in as many words), so it has to be asked first. Taking the preset here is how a
        // Monster Hunter Wilds registered with mod folder "mods" - a folder that does not exist on
        // an RE Engine game - and then reported no mods for a library the user had already
        // downloaded. 54 of the 118 feed games with a curated path differ from their preset, so this
        // was never one game's problem. Preset stays as the fallback for everything uncurated.
        var modPath = KnownModPaths.ByAppId(game.AppId)
                      ?? (EnginePresets.Presets.TryGetValue(engine, out var preset) ? preset.ModPath : null);
        var input = new GameInput
        {
            // WHICH curated game this is, rather than letting BuildGameEntry infer it from the display
            // name. Slugify(name) and the manifest id agree by luck, not by rule: "Minecraft: Java
            // Edition" produces minecraft-java-edition and matches the `minecraft` entry not at all,
            // which silently discards every curated fact about the game. Null when the manifest does
            // not know this app id, which leaves the name-derived fallback exactly as it was.
            Id = ManifestIdLookup.BySteamAppId(Manifest.EffectiveManifest.Current, game.AppId),
            Name = game.Name,
            Engine = engine,
            GameRoot = game.GameRoot,
            SteamAppId = game.AppId,
            ModPath = modPath,
        };
        return new SteamImportPlan(game.Name, game.AppId, Addable: true, Engine: engine, Input: input);
    }
}
