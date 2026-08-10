using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Discovery;
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
        // `exists` is not decoration. A declared location can be pure fiction — an Elden Ring
        // registration declares a Mod Engine 2 `mod` folder that is not on disk while every mod
        // loads fine by direct-inject. Reporting the declared path with no reality check invites a
        // caller to conclude the install is broken. See get_game_shape for the full picture.
        return new
        {
            gameId = game.Id,
            gameRoot = c.GameRoot,
            dataDir = c.DataDir,
            // RESOLVED, not stored — every other field here comes from `c`, and this tool's contract
            // is "the resolved mod context". A stale registration stores the engine preset's frozen
            // default while the manifest has since corrected it; reporting the stored list would tell
            // an agent this game scans ["pak"] while the scanner is keying on ["archive"], and the
            // agent would reason about the wrong file type entirely. Call get_game_shape for the
            // declared-vs-actual picture; this field answers "what does the launcher look for".
            fileExtensions = c.DeclaredExts,
            groupingRule = c.GroupingRule,
            modLocations = c.Locations.Select(l => new
            {
                name = l.Name,
                label = game.ModLocations.FirstOrDefault(m => m.Name == l.Name)?.Label ?? l.Name,
                path = game.ModLocations.FirstOrDefault(m => m.Name == l.Name)?.Path ?? l.Name,
                absolute = l.Abs,
                exists = !string.IsNullOrEmpty(l.Abs) && Directory.Exists(l.Abs),
            }).ToArray(),
            hint = "Declared locations are what the registration CLAIMS. Call get_game_shape to see "
                   + "where mods actually are and whether the two agree.",
        };
    }

    [McpServerTool(Name = "get_game_shape")]
    [Description("What a game's registration claims versus where its mods actually are: the listing "
               + "mechanism, the play folder, declared locations with an exists flag, the real "
               + "directories mods were found in, any loaders present, and an alignment verdict with "
               + "plain-language notes. Start here when something looks wrong — drift is common and "
               + "usually harmless, and this says which it is.")]
    public static object GetGameShape([Description("The game id, from list_games.")] string gameId)
    {
        var game = Find(gameId);
        if (game is null) return UnknownGame(gameId);
        var s = GameShape.Of(game);
        return new
        {
            gameId = s.GameId,
            managed = s.Managed,
            modCount = s.ModCount,
            mechanism = s.Mechanism.ToString(),
            alignment = s.Alignment.ToString(),
            gameRoot = s.GameRoot,
            playFolder = s.PlayFolder,
            declaredLocations = s.DeclaredLocations.Select(d => new
            {
                name = d.Name, path = d.Path, absolute = d.Absolute, exists = d.Exists,
            }).ToArray(),
            contentRoots = s.ContentRoots.Select(r => new
            {
                relativePath = r.RelativePath, absolute = r.Absolute,
                fileCount = r.FileCount, insideDeclared = r.InsideDeclared,
            }).ToArray(),
            loaders = s.Loaders,
            notes = s.Notes,
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
            // `location` names the LANE ("direct-inject"), not a place — on its own it cannot answer
            // where a mod is, which is the question a caller actually has. `files` and `isLoader`
            // were on the record all along and simply never surfaced; without them a loader reads as
            // an ordinary mod and its folder looks like an unexplained stray.
            mods = mods.Select(m => new
            {
                name = m.Name,
                displayTitle = m.DisplayName,
                enabled = m.Enabled,
                @class = m.Class,
                location = m.Location,
                files = m.Files,
                isFolder = m.IsFolder,
                isLoader = m.IsLoader,
                loader = m.Loader,
                author = m.Author,
                sourceUrl = m.ModUrl,
            }).ToArray(),
            hint = "`location` is the listing lane, not a path. File paths are relative to the play "
                   + "folder (loose lanes) or the mod's declared location — get_game_shape resolves both.",
        });
    }

    private static GameEntry? Find(string gameId)
        => RegistryStore.Load(McpConfig.DataRoot).Games.FirstOrDefault(g => g.Id == gameId);

    private static object UnknownGame(string gameId)
        => new { error = new { code = "unknown_game", message = $"No registered game with id '{gameId}'.", hint = "Call list_games for valid ids." } };
}
