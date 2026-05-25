namespace ModManager.Core;

/// <summary>
/// Builds the prompt a user hands to any LLM to author a game registration profile. The model
/// returns JSON on the structured contract (relative/enum values only), which the launcher
/// validates (<see cref="GameProfileImport"/>) and resolves. Twin of <see cref="ThemePrompt"/> —
/// not agentic: the app crafts the ask and validates the answer.
/// </summary>
public static class GameProfilePrompt
{
    public static string Build(string? gameName)
    {
        var g = string.IsNullOrWhiteSpace(gameName) ? "the game" : gameName.Trim();
        var engines = string.Join(", ", EnginePresets.Presets.Keys);
        return
            "You are filling a registration profile for a PC game mod launcher.\n" +
            $"Game: {g}\n\n" +
            "Return ONLY a single JSON object - no prose, no markdown fences. Use STRUCTURED, RELATIVE\n" +
            "values only - NEVER an absolute machine path like C:\\Users\\... The app resolves real paths.\n\n" +
            "Fields:\n" +
            "  name (string),\n" +
            $"  engine (one of: {engines}),\n" +
            "  windowTitle (string, optional),\n" +
            "  steamAppId (string of digits, optional),\n" +
            "  modPath (string, relative to the install folder; optional - omit to use the engine default),\n" +
            "  fileExtensions (array of strings, optional), groupingRule (string, optional),\n" +
            "  saveRoot (one of: DocumentsMyGames, AppData, LocalAppData, SteamUserData, GameInstall),\n" +
            "  saveSubPath (string, relative path under saveRoot),\n" +
            "  requiredLauncher (string, relative path to the launcher exe that must be used when modded; optional),\n" +
            "  launchTargets (array of objects { label, kind: \"steam\" or \"exe\", target, isDefault }; optional),\n" +
            "  curseforgeGameId (number, optional).\n\n" +
            "Rules: valid JSON only; engine and saveRoot must be from the lists above; every path relative.";
    }
}
