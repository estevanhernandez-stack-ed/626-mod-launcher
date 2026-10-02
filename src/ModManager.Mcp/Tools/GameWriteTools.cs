using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Agent;

namespace ModManager.Mcp.Tools;

/// <summary>
/// Adding a game and uninstalling a mod (E1, fifth slice), through the Core the app uses:
/// <see cref="GameRegistration.Add"/> is the app's add (detect the mod folders and launchers, never add
/// one install twice, make it active), and <see cref="ModUninstall"/> is the app's uninstall rule and
/// deletes. Uninstall is the one destructive mod operation, so it takes <c>confirm: true</c> (agent-access
/// law 2) and refuses what the app never offers to delete. Every call is audited.
/// </summary>
[McpServerToolType]
public static class GameWriteTools
{
    [McpServerTool(Name = "register_game")]
    [Description("Add an installed game to the launcher, the way the app's add does: it finds where the game's "
                 + "mods already live and how to launch it with mods (Mod Engine 2, Seamless Co-op), creates the "
                 + "declared mod folder when the game definition names one, and makes it the active game. An install "
                 + "that is already registered is never added twice; it is switched to and alreadyRegistered says so. "
                 + "With a Steam or EA id it adds through the same curated lookups as the app's store add. Leave engine empty "
                 + "to detect it; when it can't be told, this refuses rather than guess. Recorded in the game's agent-log.jsonl "
                 + "(a refusal, which names no game yet, in the launcher's).")]
    public static object RegisterGame(
        [Description("The game's display name.")] string name,
        [Description("Absolute path of the game's install folder.")] string gameRoot,
        [Description("Engine key: ue-pak, bethesda, fromsoft, bepinex, smapi, minecraft, source, melonloader or custom. "
                     + "Empty to detect it from the folder, as the app's add dialog does.")] string? engine = null,
        [Description("The Steam app id, when it is a Steam game.")] string? steamAppId = null,
        [Description("The EA app content id, when it is an EA app game.")] string? eaContentId = null)
    {
        const string tool = "register_game";
        var args = new Dictionary<string, string>
        {
            ["name"] = name, ["gameRoot"] = gameRoot, ["engine"] = engine ?? "",
            ["steamAppId"] = steamAppId ?? "", ["eaContentId"] = eaContentId ?? "",
        };

        // Refusals here name no game yet, so they go to the launcher-level log beside games.json.
        object RefuseHere(AgentRefusal r, string detail) => WriteTools.Refuse(tool, McpConfig.DataRoot, "", args, r, detail);

        // Absolute only, and stored normalised: a relative path resolves against whichever process
        // reads it, and the app's working folder is not this server's.
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot.Trim())
            || !Directory.Exists(gameRoot.Trim()))
            return RefuseHere(AgentRefusal.NotFound,
                $"No game folder at '{gameRoot}'. Pass the ABSOLUTE path of the installed game's folder and a name.");
        var root = Path.GetFullPath(gameRoot.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var explicitEngine = string.IsNullOrWhiteSpace(engine) ? null : engine.Trim().ToLowerInvariant();
        if (explicitEngine is not null && !EnginePresets.Presets.ContainsKey(explicitEngine))
            return RefuseHere(AgentRefusal.None,
                $"'{explicitEngine}' is not an engine 626 knows. Known: {string.Join(", ", EnginePresets.Presets.Keys)}.");

        // The same planners as the app's store adds, so the curated manifest facts (id, engine, mod
        // folder, ban risk) come with the game exactly as they would through the app.
        GameInput? input;
        string engineSource;
        if (!string.IsNullOrWhiteSpace(eaContentId))
        {
            input = ModManager.Core.Stores.EaGameImport.Plan(
                new InstalledGame(ModManager.Core.Stores.EaInstallScan.StoreKind, eaContentId.Trim(), name.Trim(), root),
                ModManager.Core.Manifest.EffectiveManifest.Current.Games,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            if (input is null)
                return RefuseHere(AgentRefusal.None,
                    $"'{eaContentId}' is not an EA app game 626 knows. The app adds EA games only from its curated list.");
            engineSource = "curated";
        }
        else if (!string.IsNullOrWhiteSpace(steamAppId))
        {
            var plan = SteamGameImport.Plan(new SteamImportCandidate(steamAppId.Trim(), name.Trim(), root),
                explicitEngine ?? EngineScan.Detect(root));
            if (!plan.Addable || plan.Input is null)
                return RefuseHere(AgentRefusal.None,
                    $"626 couldn't tell {name}'s engine from its Steam id or its folder. Pass engine.");
            input = plan.Input;
            engineSource = KnownEngines.ByAppId(steamAppId.Trim()) is not null ? "curated" : explicitEngine is not null ? "given" : "detected";
        }
        else
        {
            var resolvedEngine = explicitEngine ?? EngineScan.Detect(root);
            if (resolvedEngine is null)
                return RefuseHere(AgentRefusal.None,
                    $"626 couldn't tell {name}'s engine from its folder, and registering it as 'custom' would guess. Pass engine.");
            input = new GameInput { Name = name.Trim(), GameRoot = root, Engine = resolvedEngine };
            engineSource = explicitEngine is not null ? "given" : "detected";
        }

        GameEntry entry;
        bool already;
        try { entry = GameRegistration.Add(McpConfig.DataRoot, input, out already); }
        catch (Exception e)
        {
            var detail = ErrorRemedy.Describe(e);
            AgentAudit.Append(McpConfig.DataRoot, new AgentAuditEntry(DateTime.UtcNow, tool, "", args, "error", detail));
            return new { ok = false, refusal = "error", detail };
        }

        var dataDir = Scanner.DataDirForGame(entry);
        AgentAudit.Append(dataDir, new AgentAuditEntry(DateTime.UtcNow, tool, entry.Id, args,
            already ? "already_registered" : "ok", already ? $"{entry.GameName} was already registered; switched to it." : $"Added {entry.GameName}."));

        return new
        {
            ok = true,
            gameId = entry.Id,
            name = entry.GameName,
            alreadyRegistered = already,
            madeActive = true,
            engine = entry.Engine,
            engineSource,
            modLocations = entry.ModLocations.Select(l => new
            {
                name = l.Name,
                path = l.Path,
                exists = !string.IsNullOrEmpty(entry.GameRoot) && Directory.Exists(Path.Combine(entry.GameRoot, l.Path)),
            }).ToArray(),
            launchTargets = entry.LaunchTargets.Select(t => new { label = t.Label, kind = t.Kind }).ToArray(),
            modEngineConfig = entry.ModEngineConfig,
            saveDir = entry.SaveDir,
            hint = already
                ? "Nothing was added; this install was already in the library under that id."
                : "The app also searches for the save folder and sweeps for mods already installed when a person adds a game; "
                  + "this add does neither. Call get_game_shape to check where mods are, and list_mods to see them.",
        };
    }

    [McpServerTool(Name = "uninstall_mod")]
    [Description("Permanently delete a mod: its files in every mod location and mirror, any held (turned-off) copy, and "
                 + "626's record of it; for a Mod Engine 2 game, its folder and its line in the config. Cannot be undone, "
                 + "so it refuses without confirm: true and then lists what it would delete. Never deletes loose files in "
                 + "the game folder (direct-inject, loose-root: turn those off instead) or a mod another tool manages. "
                 + "Recorded in the game's agent-log.jsonl.")]
    public static object UninstallMod(
        [Description("The game id, from list_games.")] string gameId,
        [Description("The mod's name as list_mods reports it.")] string modName,
        [Description("Must be true. Uninstall deletes files and cannot be undone.")] bool confirm = false)
    {
        const string tool = "uninstall_mod";
        var args = new Dictionary<string, string> { ["modName"] = modName, ["confirm"] = confirm.ToString() };

        var game = ModTools.Find(gameId);
        if (game is null)
            return WriteTools.Refuse(tool, null, gameId, args, AgentRefusal.NotFound, $"No game with id '{gameId}'.");
        var ctx = Scanner.GameContext(game);
        var mod = ModListing.Resolve(game).FirstOrDefault(m => string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.NotFound,
                $"'{modName}' is not a mod in {gameId}. Call list_mods for the names.");

        if (ModUninstall.Refusal(ctx, mod) is { } why)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args,
                why.Kind == UninstallBlock.ManagedByAnotherTool ? AgentRefusal.ManagedByAnotherTool : AgentRefusal.None, why.Message);

        if (!confirm)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.ConfirmationRequired,
                $"Uninstalling {mod.Name} deletes {mod.Files.Count} file(s): {string.Join(", ", mod.Files.Take(10))}"
                + (mod.Files.Count > 10 ? ", …" : "") + ". It cannot be undone; turning it off is reversible. "
                + "Call again with confirm: true to delete.");

        try { ModUninstall.Run(ctx, mod); }
        catch (Exception e)
        {
            var detail = ErrorRemedy.Describe(e);
            AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "error", detail));
            return new { ok = false, refusal = "error", detail };
        }

        // Check, don't assume: the mod must be gone from the listing.
        if (ModListing.Resolve(game).Any(m => string.Equals(m.Name, mod.Name, StringComparison.OrdinalIgnoreCase)))
        {
            var notApplied = $"Tried to uninstall {mod.Name}, but the mod list still shows it.";
            AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "not_applied", notApplied));
            return new { ok = false, refusal = "not_applied", detail = notApplied };
        }

        AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "ok", $"Uninstalled {mod.Name}."));
        return new { ok = true, gameId, modName = mod.Name, deleted = mod.Files, detail = $"Uninstalled {mod.Name}." };
    }
}
