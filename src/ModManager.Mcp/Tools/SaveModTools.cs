using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Agent;

namespace ModManager.Mcp.Tools;

/// <summary>
/// Save/world mods for an agent. The list (E1, third slice) is what the Saves dialog shows, read from the
/// same store (<see cref="SaveModStore.Load"/>) in the same order, plus the reasons around it. Install,
/// reset and remove (E1, seventh slice) go through the Core the app uses (<see cref="SaveModFlow"/>,
/// <see cref="SaveModInstaller"/>), so each snapshots what it can lose before it writes and never touches
/// a game-managed folder. Each refuses where the app would, and also where the user's ban-risk
/// acknowledgment is missing, which an agent can never give. Reset and remove take <c>confirm: true</c>.
/// Every write is audited.
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
                .Select(e => (Entry: e, Kept: SaveModStore.KeptZip(ctx.DataDir, e)))
                .Select(x => new
                {
                    worldId = x.Entry.Guid,
                    name = x.Entry.Name,
                    sourceZip = x.Entry.SourceZip,
                    // The zip a reset reads, the way the app's reset finds it (an older build's copy counts
                    // when it still holds this world).
                    keptZip = x.Kept,
                    sourceZipKept = x.Kept is not null,
                    installedUtc = x.Entry.InstalledUtc,
                })
                .ToArray(),
            hint = "Resetting a world reinstalls it from its kept zip; when sourceZipKept is false the world "
                   + "can't be reset. install_save_mod, reset_save_mod and remove_save_mod change these.",
        };
    }

    [McpServerTool(Name = "install_save_mod")]
    [Description("Install one save/world mod zip (a Worlds/<id> package) into the game's save folder, as dropping "
                 + "it on the app does: what it can lose is snapshotted first, game-managed folders are never "
                 + "written, and a copy of the zip is kept so the world can be reset later. Refuses a zip that "
                 + "isn't a save mod (use intake for regular mods), a world that is already installed (use "
                 + "reset_save_mod), and a game whose saves 626 may not write or whose ban-risk acknowledgment the "
                 + "user hasn't given. Not idempotent: a second call for the same world is refused. Audited.")]
    public static object InstallSaveMod(
        [Description("The game id, from list_games.")] string gameId,
        [Description("Absolute path of the save mod zip.")] string zipPath)
    {
        const string tool = "install_save_mod";
        var args = new Dictionary<string, string> { ["zipPath"] = zipPath ?? "" };
        if (Prelude(tool, gameId, args, out var game, out var ctx) is { } refused) return refused;

        var zip = zipPath?.Trim() ?? "";
        if (zip.Length == 0 || !Path.IsPathRooted(zip) || !File.Exists(zip))
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.NotFound,
                $"No file at '{zipPath}'. Pass the ABSOLUTE path of the save mod zip.");
        zip = Path.GetFullPath(zip);

        var exts = GameSaveTypesCatalog.Resolve(game).SaveTypes.Select(t => t.Extension).ToList();
        var verdict = SaveModFlow.Classify(zip, exts, writeRefusal: null);
        if (verdict.Outcome == SaveModDropOutcome.NotASaveMod)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.None,
                $"{Path.GetFileName(zip)} is not a save/world mod (no Worlds/<id> folder or save files in it). "
                + "To install a regular mod, use intake.");
        if (verdict.Outcome == SaveModDropOutcome.Failed || verdict.WorldGuid is null)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.None,
                verdict.Reason ?? "626 couldn't tell which world this zip installs.");
        args["worldId"] = verdict.WorldGuid;

        if (WorldDir(tool, ctx, game, gameId, args, verdict.WorldGuid, out var worldDir) is { } noFolder) return noFolder;

        // Core refuses a world that is already there, before it writes anything, and says whether 626 installed it.
        var v = SaveModFlow.TryHandleDrops(new[] { zip }, exts,
            saveProfilesDir: ctx.SaveDir!, snapshotsDir: ctx.SavesDir, dataDir: ctx.DataDir,
            saveModPath: game.SaveModPath, forbidden: game.SaveModForbidden,
            writeAllowed: true, writeRefusal: null).Single();
        if (v.Outcome == SaveModDropOutcome.AlreadyInstalled)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, "already_installed",
                (v.Reason ?? "") + " (Agent tools: reset_save_mod, remove_save_mod.)");
        if (v.Outcome != SaveModDropOutcome.Installed)
            return Error(tool, ctx, gameId, args, v.Reason ?? v.Outcome.ToString());

        // Check, don't assume: the world is in the save folder, recorded, and resettable.
        var entry = SaveModStore.Load(ctx.DataDir).FirstOrDefault(e => string.Equals(e.Guid, v.WorldGuid, StringComparison.OrdinalIgnoreCase));
        var kept = entry is null ? null : SaveModStore.KeptZip(ctx.DataDir, entry);
        if (entry is null || kept is null || !HoldsFiles(worldDir))
            return Error(tool, ctx, gameId, args,
                $"Installed {v.WorldGuid}, but 626 can't confirm it: world folder present {Directory.Exists(worldDir)}, "
                + $"recorded {entry is not null}, kept zip {kept is not null}. Open Saves in the app to check.");

        var done = $"Installed {entry.Name} (world {entry.Guid}). " + SnapshotNote(ctx, game, worldDir, entry.Guid, newWorld: true);
        AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "ok", done));
        return new
        {
            ok = true, gameId, worldId = entry.Guid, name = entry.Name, installedTo = worldDir, keptZip = kept,
            detail = done,
            hint = "reset_save_mod starts this world over from keptZip; remove_save_mod deletes it. Both snapshot the "
                   + "world first and take confirm: true; each result says where that snapshot is.",
        };
    }

    [McpServerTool(Name = "reset_save_mod")]
    [Description("Start an installed save/world mod over: its world folder is replaced with a fresh copy from the zip "
                 + "kept at install, as the Saves dialog's Reset does. Progress in that world is lost, so it refuses "
                 + "without confirm: true (then it says what it would do). It is snapshotted first, and the result says "
                 + "where that snapshot is. Refuses when the kept zip is gone, and where install_save_mod would. "
                 + "Audited.")]
    public static object ResetSaveMod(
        [Description("The game id, from list_games.")] string gameId,
        [Description("The world id, from list_save_mods.")] string worldId,
        [Description("Must be true. Resetting discards the world's progress (a snapshot is taken first).")] bool confirm = false)
    {
        const string tool = "reset_save_mod";
        var args = new Dictionary<string, string> { ["worldId"] = worldId ?? "", ["confirm"] = confirm.ToString() };
        if (Prelude(tool, gameId, args, out var game, out var ctx) is { } refused) return refused;
        if (FindEntry(tool, ctx, gameId, args, worldId, out var entry) is { } missing) return missing;

        if (SaveModStore.KeptZip(ctx.DataDir, entry) is not { } kept)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.None,
                $"{entry.Name} can't be reset: the zip it was installed from is gone. Nothing was changed.");
        if (WorldDir(tool, ctx, game, gameId, args, entry.Guid, out var worldDir) is { } noFolder) return noFolder;

        if (!confirm)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.ConfirmationRequired,
                $"Resetting {entry.Name} deletes {worldDir} and extracts a fresh copy from {kept}. Progress in that world "
                + "is lost. " + SnapshotNote(ctx, game, worldDir, entry.Guid, newWorld: false)
                + " Call again with confirm: true to reset.");

        try { SaveModInstaller.ResetWorld(ctx.SaveDir!, ctx.SavesDir, kept, entry.Guid, game.SaveModPath, game.SaveModForbidden); }
        catch (Exception e) { return Error(tool, ctx, gameId, args, ErrorRemedy.Describe(e)); }

        if (!HoldsFiles(worldDir))
            return Error(tool, ctx, gameId, args, $"Reset {entry.Name}, but its world folder {worldDir} is empty afterwards. "
                                                 + SnapshotNote(ctx, game, worldDir, entry.Guid, newWorld: false));

        var done = $"Reset {entry.Name} (world {entry.Guid}) from its kept zip. " + SnapshotNote(ctx, game, worldDir, entry.Guid, newWorld: false);
        AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "ok", done));
        return new { ok = true, gameId, worldId = entry.Guid, name = entry.Name, worldDir, resetFrom = kept, detail = done };
    }

    [McpServerTool(Name = "remove_save_mod")]
    [Description("Remove an installed save/world mod: its world folder is deleted from the save folder and 626 stops "
                 + "listing it, as the Saves dialog's Remove does. Refuses without confirm: true (then it says what it "
                 + "would delete). It is snapshotted first, and the result says where that snapshot is; the zip "
                 + "kept for resetting it is deleted. Refuses an EA game or no save folder, but not for ban risk: removing "
                 + "a mod is the safe direction. Audited.")]
    public static object RemoveSaveMod(
        [Description("The game id, from list_games.")] string gameId,
        [Description("The world id, from list_save_mods.")] string worldId,
        [Description("Must be true. Removing deletes the world from the save folder (a snapshot is taken first).")] bool confirm = false)
    {
        const string tool = "remove_save_mod";
        var args = new Dictionary<string, string> { ["worldId"] = worldId ?? "", ["confirm"] = confirm.ToString() };
        // No ban-risk gate: removing a world mod moves the game back toward vanilla, and getting safer needs no
        // friction (WriteTools). The EA refusal and the save folder still apply.
        if (Prelude(tool, gameId, args, out var game, out var ctx, gateBanRisk: false) is { } refused) return refused;
        if (FindEntry(tool, ctx, gameId, args, worldId, out var entry) is { } missing) return missing;

        if (WorldDir(tool, ctx, game, gameId, args, entry.Guid, out var worldDir) is { } noFolder) return noFolder;

        if (!confirm)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.ConfirmationRequired,
                $"Removing {entry.Name} deletes {worldDir} and stops listing it. " + SnapshotNote(ctx, game, worldDir, entry.Guid, newWorld: false)
                + " Call again with confirm: true to remove.");

        try
        {
            SaveModInstaller.RemoveWorld(ctx.SaveDir!, ctx.SavesDir, entry.Guid, game.SaveModPath, game.SaveModForbidden);
            SaveModStore.Forget(ctx.DataDir, entry.Guid);   // unlisted, and its kept zip with it, as the Saves dialog does
        }
        catch (Exception e) { return Error(tool, ctx, gameId, args, ErrorRemedy.Describe(e)); }

        var stillListed = SaveModStore.Load(ctx.DataDir).Any(e => string.Equals(e.Guid, entry.Guid, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(worldDir) || stillListed)
            return Error(tool, ctx, gameId, args, $"Removed {entry.Name}, but its world folder is still there "
                                                 + $"({Directory.Exists(worldDir)}) or it is still listed ({stillListed}).");

        var done = $"Removed {entry.Name} (world {entry.Guid}). " + SnapshotNote(ctx, game, worldDir, entry.Guid, newWorld: false);
        AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "ok", done));
        return new { ok = true, gameId, worldId = entry.Guid, name = entry.Name, deleted = worldDir, detail = done };
    }

    // The checks every save write makes, in the order the app's would: the game exists; its saves are 626's to
    // write (EA cloud saves are not, and no acknowledgment changes that); 626 knows the save folder; and on a
    // high ban-risk game the user has acknowledged save writes. That last one only the user can give, in the app.
    private static object? Prelude(string tool, string gameId, Dictionary<string, string> args, out GameEntry game, out GameContext ctx,
                                   bool gateBanRisk = true)
    {
        game = ModTools.Find(gameId)!;
        ctx = null!;
        if (game is null)
            return WriteTools.Refuse(tool, null, gameId, args, AgentRefusal.NotFound, $"No game with id '{gameId}'. Call list_games.");
        ctx = Scanner.GameContext(game);
        if (SaveWritePolicy.Refusal(game) is { } refusal)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.None, refusal);
        if (string.IsNullOrEmpty(ctx.SaveDir))
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.None,
                "626 doesn't know this game's save folder, so there is nowhere to write. The user can set it in Saves.");
        if (gateBanRisk && SaveWritePolicy.NeedsAcknowledgment(game, ctx.DataDir))
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.BanRiskNotAcknowledged,
                $"{game.GameName} is high ban-risk, so writing its saves asks the user first, and an agent can't answer "
                + "for them. Ask them to do this in the app's Saves dialog.");
        return null;
    }

    private static object? FindEntry(string tool, GameContext ctx, string gameId, Dictionary<string, string> args,
                                     string? worldId, out SaveModEntry entry)
    {
        entry = SaveModStore.Load(ctx.DataDir).FirstOrDefault(e => string.Equals(e.Guid, worldId?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return entry is null
            ? WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.NotFound,
                $"No save mod with world id '{worldId}' in {gameId}. Call list_save_mods for the ids.")
            : null;
    }

    // The world's folder under the save tree, looked up without writing anything. A profile that can't be read,
    // or isn't one profile, is refused here and audited like every other refusal.
    private static object? WorldDir(string tool, GameContext ctx, GameEntry game, string gameId, Dictionary<string, string> args,
                                    string worldGuid, out string worldDir)
    {
        try
        {
            worldDir = SaveModInstaller.WorldDirFor(ctx.SaveDir!, game.SaveModPath, game.SaveModForbidden, worldGuid);
            return null;
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            worldDir = "";
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.None, ErrorRemedy.Describe(e));
        }
    }

    // Whether the world folder has anything in it, for the checks after a write. Unreadable counts as no: the
    // write can't be confirmed, and the result says so.
    private static bool HoldsFiles(string dir)
    {
        try { return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    // Where this write's undo lives, in words, by the rule SaveModInstaller.SnapshotBeforeWrite follows.
    private static string SnapshotNote(GameContext ctx, GameEntry game, string worldDir, string worldGuid, bool newWorld)
    {
        if (SaveModSnapshots.OutsideSavesList(ctx.SaveDir!, ctx.SavesDir, game.SaveModPath, game.SaveModForbidden, worldGuid) is not { } dir)
            return "626 snapshots the save folder first, so it can be restored from Saves.";
        if (newWorld)
            return "It is a new world, so there was nothing to snapshot; remove_save_mod undoes it.";
        return $"626 snapshots this world first, into {dir}. Saves doesn't list those: to undo, unzip the newest one "
               + $"into {worldDir}.";
    }

    private static object Error(string tool, GameContext ctx, string gameId, Dictionary<string, string> args, string detail)
        => WriteTools.Refuse(tool, ctx.DataDir, gameId, args, "error", detail);
}

