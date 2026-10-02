using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;

namespace ModManager.Mcp.Tools;

/// <summary>
/// Installed save/world mods for an agent (E1, third slice): the list the Saves dialog shows, read from
/// the same store (<see cref="SaveModStore.Load"/>) in the same order, plus the reasons around it:
/// whether each kept source zip is still there (resetting a world needs it), and whether a new save
/// mod could be installed for this game at all.
/// </summary>
[McpServerToolType]
public static class SaveModTools
{
    [McpServerTool(Name = "list_save_mods")]
    [Description("The save/world mods installed for a game, newest first, as the Saves dialog lists them: "
                 + "world id, name, the source zip kept for resetting the world, and when it was installed. "
                 + "Also says whether the kept zip is still on disk, and whether installing a new save mod "
                 + "is possible for this game (no known save folder, saves the app may not write, or a "
                 + "ban-risk acknowledgment the user has not given).")]
    public static object ListSaveMods([Description("The game id, from list_games.")] string gameId)
    {
        var game = ModTools.Find(gameId);
        if (game is null) return ModTools.UnknownGame(gameId);
        // For every game whose saves the app may write, the folder is game.SaveDir, which this context
        // carries (the app's SaveLocator returns it unchanged for those games).
        var ctx = Scanner.GameContext(game);

        // The EA refusal first: setting a save folder would not make those saves writable.
        var refusal = SaveWritePolicy.Refusal(game);
        string? installBlocked = refusal
            ?? (string.IsNullOrEmpty(ctx.SaveDir) ? "626 doesn't know this game's save folder, so a save mod has nowhere to go." : null);

        return new
        {
            ok = true,
            gameId = game.Id,
            saveDir = ctx.SaveDir,
            // The Saves dialog narrows an EA game's folder from its curated hint (App-side SaveLocator);
            // the registry value here can be the parent of it. Such a game refuses save writes anyway.
            saveDirNote = refusal is null ? null : "For an EA app game the Saves dialog can show a narrower folder than this registered one.",
            installBlocked,
            // Only meaningful when nothing blocks the install: a high ban-risk game asks before writing
            // a new save mod until the user ticks "don't ask again" in the app. An agent cannot give it.
            installNeedsAcknowledgment = installBlocked is null && SaveWritePolicy.NeedsAcknowledgment(game, ctx.DataDir),
            saveMods = SaveModStore.Load(ctx.DataDir)
                .OrderByDescending(e => e.InstalledUtc)
                .Select(e => new
                {
                    worldId = e.Guid,
                    name = e.Name,
                    sourceZip = e.SourceZip,
                    sourceZipKept = !string.IsNullOrEmpty(e.SourceZip) && File.Exists(e.SourceZip),
                    installedUtc = e.InstalledUtc,
                })
                .ToArray(),
            hint = "Resetting a world reinstalls it from its kept source zip; when sourceZipKept is false "
                   + "the world can't be reset.",
        };
    }
}
