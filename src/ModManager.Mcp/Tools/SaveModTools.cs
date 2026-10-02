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
        // The app narrows an EA game's save folder (SaveLocator, App-side); for every game whose saves
        // it may write, the folder is game.SaveDir, which is what this context carries. EA games refuse
        // the install either way, and installBlocked says so.
        var ctx = Scanner.GameContext(game);

        var refusal = SaveWritePolicy.Refusal(game);
        var gated = BanRiskRules.ShouldGateSaveWrite(
            BanRiskCatalog.Effective(game), BanRiskAckStore.IsAcked(ctx.DataDir, game.Id, BanRiskAck.WriteSaves));
        string? installBlocked =
            string.IsNullOrEmpty(ctx.SaveDir) ? "626 doesn't know this game's save folder, so a save mod has nowhere to go."
            : refusal;

        return new
        {
            ok = true,
            gameId = game.Id,
            saveDir = ctx.SaveDir,
            installBlocked,
            // Only meaningful when nothing blocks the install: a high ban-risk game asks before writing
            // a new save mod until the user ticks "don't ask again" in the app. An agent cannot give it.
            installNeedsAcknowledgment = installBlocked is null && gated,
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
