using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Mcp.Tools;

/// <summary>Read-only per-game queries — the resolved mod context and the detected mod list. Both run
/// against the same pure Core (Scanner) the app uses, so the MCP never holds a second source of truth.</summary>
[McpServerToolType]
public static class ModTools
{
    [McpServerTool(Name = "get_mod_context")]
    [Description("Returns the resolved mod context for a game: game root, data dir, mod locations, file extensions, grouping rule.")]
    public static object GetModContext([Description("The game id, from list_games.")] string gameId)
    {
        var game = Find(gameId);
        if (game is null) return UnknownGame(gameId);
        var c = Scanner.GameContext(game);
        return new
        {
            gameId = game.Id,
            gameRoot = c.GameRoot,
            dataDir = c.DataDir,
            fileExtensions = game.FileExtensions,
            groupingRule = game.GroupingRule,
            modLocations = game.ModLocations.Select(l => new { name = l.Name, label = l.Label, path = l.Path }).ToArray(),
        };
    }

    [McpServerTool(Name = "list_mods")]
    [Description("Lists every detected mod for a game with its enabled state, class/chip, location, loader, and metadata (display title / author / source URL).")]
    public static Task<object> ListMods([Description("The game id, from list_games.")] string gameId)
    {
        var game = Find(gameId);
        if (game is null) return Task.FromResult(UnknownGame(gameId));
        var mods = ModListing.Resolve(game);
        return Task.FromResult<object>(new
        {
            gameId = game.Id,
            mods = mods.Select(m => new
            {
                name = m.Name,
                displayTitle = m.DisplayName,
                enabled = m.Enabled,
                @class = m.Class,
                location = m.Location,
                loader = m.Loader,
                author = m.Author,
                sourceUrl = m.ModUrl,
            }).ToArray(),
        });
    }

    private static GameEntry? Find(string gameId)
        => RegistryStore.Load(McpConfig.DataRoot).Games.FirstOrDefault(g => g.Id == gameId);

    private static object UnknownGame(string gameId)
        => new { error = new { code = "unknown_game", message = $"No registered game with id '{gameId}'.", hint = "Call list_games for valid ids." } };
}
