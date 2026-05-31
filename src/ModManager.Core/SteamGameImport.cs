namespace ModManager.Core;

/// <summary>One installed Steam game the importer can plan an auto-add for. Mirrors the App's
/// SteamGame (appId + display name + resolved install folder) but stays in Core so the planning
/// logic is headless-testable.</summary>
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

        var modPath = EnginePresets.Presets.TryGetValue(engine, out var preset) ? preset.ModPath : null;
        var input = new GameInput
        {
            Name = game.Name,
            Engine = engine,
            GameRoot = game.GameRoot,
            SteamAppId = game.AppId,
            ModPath = modPath,
        };
        return new SteamImportPlan(game.Name, game.AppId, Addable: true, Engine: engine, Input: input);
    }
}
