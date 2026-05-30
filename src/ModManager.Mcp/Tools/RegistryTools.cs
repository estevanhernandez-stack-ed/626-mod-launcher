using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Mcp.Tools;

/// <summary>Read-only registry queries — which games the launcher knows about, and which is active.
/// Both read games.json headlessly via the shared Core RegistryStore.</summary>
[McpServerToolType]
public static class RegistryTools
{
    [McpServerTool(Name = "list_games")]
    [Description("Lists every game registered in the launcher (id, name, engine, game root) plus the active game id.")]
    public static object ListGames()
    {
        var reg = RegistryStore.Load(McpConfig.DataRoot);
        return new
        {
            activeGameId = reg.ActiveGameId,
            games = reg.Games.Select(g => new
            {
                id = g.Id, gameName = g.GameName, engine = g.Engine, gameRoot = g.GameRoot,
            }).ToArray(),
        };
    }

    [McpServerTool(Name = "get_active_game")]
    [Description("Returns the launcher's currently-active game (id, name, engine, game root), or null if none.")]
    public static object? GetActiveGame()
    {
        var g = Registry.GetActiveGame(RegistryStore.Load(McpConfig.DataRoot));
        return g is null ? null : new { id = g.Id, gameName = g.GameName, engine = g.Engine, gameRoot = g.GameRoot };
    }
}
