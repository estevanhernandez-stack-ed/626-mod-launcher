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
            "  curseforgeGameId (number, optional),\n" +
            "  nexusGameDomain (string, the Nexus URL slug like 'cyberpunk2077' - not a numeric id; optional).\n\n" +
            "Rules: valid JSON only; engine and saveRoot must be from the lists above; every path relative.";
    }

    /// <summary>
    /// Builds the batched ask: one prompt that requests a JSON array of profiles, one per game,
    /// in the same order. The per-element contract is the same as <see cref="Build"/>.
    /// </summary>
    public static string BuildMany(IReadOnlyList<string> gameNames)
    {
        if (gameNames is null || gameNames.Count == 0)
            throw new ArgumentException("At least one game name is required.", nameof(gameNames));

        var engines = string.Join(", ", EnginePresets.Presets.Keys);
        var saveRoots = string.Join(", ", GameProfileImport.SaveRoots);
        var games = string.Join("\n", gameNames.Select((n, i) => $"  {i + 1}. {n.Trim()}"));
        return
            "You are filling registration profiles for multiple PC games at once.\n\n" +
            "Games (return one JSON object per game, in the same order):\n" +
            games + "\n\n" +
            "Return ONLY a single JSON array - no prose, no markdown fences. Each element is a profile\n" +
            "object using STRUCTURED, RELATIVE values only - NEVER an absolute machine path. The app\n" +
            "resolves real paths.\n\n" +
            "Per-element fields:\n" +
            "  name (string),\n" +
            $"  engine (one of: {engines}),\n" +
            "  windowTitle (string, optional),\n" +
            "  steamAppId (string of digits, optional),\n" +
            "  modPath (string, relative to the install folder; optional - omit to use the engine default),\n" +
            "  fileExtensions (array of strings, optional), groupingRule (string, optional),\n" +
            $"  saveRoot (one of: {saveRoots}),\n" +
            "  saveSubPath (string, relative path under saveRoot),\n" +
            "  requiredLauncher (string, relative path to the launcher exe that must be used when modded; optional),\n" +
            "  curseforgeGameId (number, optional),\n" +
            "  nexusGameDomain (string, the Nexus URL slug like 'cyberpunk2077' - not a numeric id; optional).\n\n" +
            "Rules: valid JSON array only; same order as the list above; engine and saveRoot must be from\n" +
            "the lists; every path relative.";
    }
}
