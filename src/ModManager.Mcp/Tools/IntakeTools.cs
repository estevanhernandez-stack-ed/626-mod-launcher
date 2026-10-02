using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Frameworks;
using ModManager.Core.Tools;

namespace ModManager.Mcp.Tools;

/// <summary>
/// Dropping files for an agent (E1, fourth slice). <c>dry_run_intake</c> returns the
/// <see cref="DropRouter"/> plan the app's own drop acts on. <c>intake</c> carries that plan out through
/// the same Core installers the app calls, under the agent rules:
/// <list type="bullet">
/// <item>The ban-risk gate refuses the whole batch on a high-risk game the user has not acknowledged,
/// exactly where the app asks; nothing is written. An agent can never give that acknowledgment.</item>
/// <item>A save mod on a game whose save writes need acknowledgment is refused for the same reason.</item>
/// <item>A framework writes into the game folder (with a backup snapshot), so it installs only with
/// <c>allowFrameworks</c>, the agent's version of the app's confirm dialog. An archive that only looks like
/// a framework goes ahead as a mod only with <c>continueAsMod</c>.</item>
/// <item>A file that would replace an existing one is skipped unless its relative path is in
/// <c>replace</c>. Replaced files are kept, as in the app, so a replace can be reverted.</item>
/// <item>Every call is audited, refusals included.</item>
/// </list>
/// </summary>
[McpServerToolType]
public static class IntakeTools
{
    [McpServerTool(Name = "dry_run_intake")]
    [Description("What dropping these files on a game would do, decided exactly as the app's own drop decides "
                 + "it, without writing anything: the lane, the ban-risk gate, and for each path whether it is a "
                 + "framework, a save mod, a UE4SS Lua mod, a tool or a regular mod. Regular mods are planned file "
                 + "by file: new, would replace an existing file, or refused. Call this before intake.")]
    public static object DryRunIntake(
        [Description("The game id, from list_games.")] string gameId,
        [Description("Absolute paths of the files or archives to drop.")] string[] paths)
    {
        var game = ModTools.Find(gameId);
        if (game is null) return ModTools.UnknownGame(gameId);
        var ctx = Scanner.GameContext(game);
        var plan = DropRouter.Plan(ctx, paths);
        return Describe(gameId, plan, paths, SaveWritePolicy.NeedsAcknowledgment(game, ctx.DataDir));
    }

    [McpServerTool(Name = "intake")]
    [Description("Install dropped files the way the app's drop does, through the same installers. Refuses the "
                 + "whole batch, writing nothing, on a high ban-risk game the user has not acknowledged in the app. "
                 + "A framework installs only with allowFrameworks; an archive that only looks like a framework "
                 + "goes ahead as a mod only with continueAsMod; a file that would replace an existing one is "
                 + "skipped unless its relPath is in replace (the old file is kept and can be reverted). Not "
                 + "idempotent: a retry can install a second copy of a tool or save mod. Call dry_run_intake "
                 + "first. Every call on a registered game is recorded in its agent-log.jsonl, refusals included.")]
    public static object Intake(
        [Description("The game id, from list_games.")] string gameId,
        [Description("Absolute paths of the files or archives to drop.")] string[] paths,
        [Description("relPaths (from dry_run_intake's collisions) to replace. Anything else that collides is skipped.")]
        string[]? replace = null,
        [Description("Install catalog frameworks found in the drop. They write into the game folder; a backup "
                     + "snapshot is taken first.")] bool allowFrameworks = false,
        [Description("Install archives that only LOOK like a framework as ordinary mods.")] bool continueAsMod = false)
    {
        const string tool = "intake";
        var args = new Dictionary<string, string>
        {
            ["paths"] = string.Join(" | ", paths),
            ["replace"] = string.Join(" | ", replace ?? Array.Empty<string>()),
            ["allowFrameworks"] = allowFrameworks.ToString(),
            ["continueAsMod"] = continueAsMod.ToString(),
        };

        var game = ModTools.Find(gameId);
        if (game is null)
            return WriteTools.Refuse(tool, null, gameId, args, AgentRefusal.NotFound, $"No game with id '{gameId}'.");
        var ctx = Scanner.GameContext(game);
        var plan = DropRouter.Plan(ctx, paths);

        if (plan.Blocked is not null)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.None, plan.Blocked);
        if (plan.NeedsBanRiskAcknowledgment)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.BanRiskNotAcknowledged,
                $"{game.GameName} is a high ban-risk game and the user has not acknowledged it in the app. Nothing "
                + "was installed. Ask the user to acknowledge the risk in the app; an agent cannot.");
        var installed = new List<object>();
        var skipped = new List<object>();
        var failed = new List<object>();
        var remaining = new List<string>();
        var replaceSet = new HashSet<string>(replace ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        // Frameworks first, in every lane, as in the app.
        foreach (var item in plan.Items)
        {
            if (item.Route == DropRoute.Framework && item.Framework is { } fw)
            {
                if (item.Problem is not null) { failed.Add(new { path = item.Path, route = "framework", reason = item.Problem }); continue; }
                if (!allowFrameworks)
                {
                    skipped.Add(new { path = item.Path, route = "framework", reason = $"{fw.DisplayName} writes into the game folder; pass allowFrameworks to install it." });
                    continue;
                }
                try
                {
                    var r = FrameworkInstaller.Install(item.Path, fw, ctx.GameRoot ?? "", ctx.DataDir,
                        game.ModLocations.Select(l => l.Path).ToList());
                    installed.Add(new { path = item.Path, route = "framework", name = fw.DisplayName, files = r.InstalledFiles.Count, into = r.InstallPath });
                }
                catch (Exception e) { failed.Add(new { path = item.Path, route = "framework", reason = e.Message }); }
                continue;
            }
            if (item.Route == DropRoute.NotInstalled)
            {
                // A Mod Engine 2 game: frameworks above still install, as in the app; mods don't.
                skipped.Add(new { path = item.Path, route = "not_installed", reason = item.Problem });
                continue;
            }
            if (item.LooksLikeFramework && !continueAsMod)
            {
                skipped.Add(new { path = item.Path, route = "looks_like_framework", reason = "Looks like a framework 626 doesn't recognise; pass continueAsMod to install it as a mod." });
                continue;
            }
            remaining.Add(item.Path);
        }

        IntakeResult? intakeResult = null;
        IReadOnlyList<IntakeCollision> keptCollisions = Array.Empty<IntakeCollision>();
        GameEntry? redetected = null;
        if (plan.Lane == DropLane.DirectInject)
        {
            var play = DirectInjectListing.PlayFolder(ctx.GameRoot);
            if (play is not null && remaining.Count > 0)
            {
                try
                {
                    var diPlan = DirectInject.Plan(play, remaining);
                    intakeResult = DirectInject.Execute(play, DirectInject.ReplacedRoot(play), diPlan, replaceSet);
                    keptCollisions = KeptCollisions(diPlan, replaceSet);
                }
                catch (Exception e) { failed.Add(new { route = "mod", reason = ErrorRemedy.Describe(e) }); }

                // Re-detect, as the app's drop does, so a new Seamless or Mod Engine 2 shows its launcher. Its
                // own try: the files have landed, and a failed re-scan must not read as a failed install.
                if (intakeResult is { } placed && (placed.Added.Count > 0 || placed.Updated.Count > 0))
                {
                    try { redetected = GameRegistration.Redetect(McpConfig.DataRoot, game.Id); }
                    catch (Exception e) { skipped.Add(new { route = "rescan", reason = "Installed, but re-detecting the game failed: " + ErrorRemedy.Describe(e) + " Run Re-scan in the app." }); }
                }
            }
        }
        else
        {
            var byPath = plan.Items.GroupBy(i => i.Path).ToDictionary(g => g.Key, g => g.First());
            var modPaths = new List<string>();
            var saveAckNeeded = SaveWritePolicy.NeedsAcknowledgment(game, ctx.DataDir);
            foreach (var p in remaining)
            {
                var item = byPath[p];
                switch (item.Route)
                {
                    case DropRoute.SaveMod:
                        if (item.Problem is not null) { failed.Add(new { path = p, route = "save_mod", reason = item.Problem }); break; }
                        if (saveAckNeeded)
                        {
                            skipped.Add(new { path = p, route = "save_mod", refusal = WriteTools.ToCode(AgentRefusal.BanRiskNotAcknowledged),
                                reason = "Installing a save mod on this high ban-risk game asks the user first. Ask them to install it in the app." });
                            break;
                        }
                        var v = SaveModFlow.TryHandleDrops(new[] { p },
                            GameSaveTypesCatalog.Resolve(game).SaveTypes.Select(t => t.Extension).ToList(),
                            saveProfilesDir: ctx.SaveDir!, snapshotsDir: ctx.SavesDir, dataDir: ctx.DataDir,
                            saveModPath: game.SaveModPath, forbidden: game.SaveModForbidden,
                            writeAllowed: true, writeRefusal: SaveWritePolicy.Refusal(game)).Single();
                        if (v.Outcome == SaveModDropOutcome.Installed) installed.Add(new { path = p, route = "save_mod", worldId = v.WorldGuid });
                        else failed.Add(new { path = p, route = "save_mod", reason = v.Reason ?? v.Outcome.ToString() });
                        break;

                    case DropRoute.Ue4ssLua:
                        // Read now: a UE4SS this same call installed above is where its Lua mods go.
                        var luaDir = DropRouter.OwnedUe4ssModsDir(ctx.DataDir);
                        if (luaDir is null)
                        {
                            skipped.Add(new { path = p, route = "ue4ss_lua", reason = item.Problem
                                ?? "A UE4SS Lua mod, but 626 doesn't manage this game's UE4SS (the UE4SS in this drop was not installed)." });
                            break;
                        }
                        try
                        {
                            var res = Ue4ssLuaInstaller.Install(p, luaDir, new SharpCompressArchiveReader());
                            installed.Add(new { path = p, route = "ue4ss_lua", name = res.ModName, into = luaDir });
                        }
                        catch (Exception e) { failed.Add(new { path = p, route = "ue4ss_lua", reason = e.Message }); }
                        break;

                    case DropRoute.Tool:
                        if (item.Problem is not null) { failed.Add(new { path = p, route = "tool", reason = item.Problem }); break; }
                        try
                        {
                            var res = ToolIntake.Install(p, ctx.DataDir, item.Tool);
                            installed.Add(new { path = p, route = "tool", name = res.Entry.DisplayName, toolId = res.Entry.ToolId });
                        }
                        catch (Exception e) { failed.Add(new { path = p, route = "tool", reason = e.Message }); }
                        break;

                    default:
                        modPaths.Add(p);
                        break;
                }
            }

            if (modPaths.Count > 0)
            {
                try
                {
                    var intakePlan = Scanner.PlanIntake(modPaths, ctx);
                    intakeResult = Scanner.ExecuteIntake(intakePlan, replaceSet, ctx);
                    keptCollisions = KeptCollisions(intakePlan, replaceSet);
                }
                catch (Exception e) { failed.Add(new { route = "mod", reason = ErrorRemedy.Describe(e) }); }
            }
        }

        // A collision left alone comes back from the installer as "kept existing" too; report it once,
        // as kept, with the relPath that would replace it.
        var keptNames = keptCollisions.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var refused = (intakeResult?.Skipped ?? new List<SkippedItem>())
            .Where(x => !(x.Reason == "kept existing" && keptNames.Contains(x.Name)))
            .Select(x => new { name = x.Name, reason = x.Reason }).ToArray();
        var outcome = failed.Count > 0 ? "partial" : "ok";
        var summary = $"installed {installed.Count}, mods added {intakeResult?.Added.Count ?? 0}, updated {intakeResult?.Updated.Count ?? 0}, "
                      + $"kept {keptCollisions.Count}, skipped {skipped.Count + refused.Length}, failed {failed.Count}";
        AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, outcome, summary));

        return new
        {
            ok = failed.Count == 0,
            gameId,
            lane = LaneName(plan.Lane),
            installed,
            mods = intakeResult is null ? null : new
            {
                added = intakeResult.Added,
                updated = intakeResult.Updated,
                kept = keptCollisions.Select(c => new { name = c.Name, relPath = c.RelPath,
                    reason = "An existing file has this name; pass its relPath in replace to replace it (the old one is kept)." }).ToArray(),
                refused,
            },
            launchTargets = redetected?.LaunchTargets.Select(t => new { label = t.Label, kind = t.Kind }).ToArray(),
            skipped,
            failed,
            detail = summary,
            hint = "Updated files keep their old version and can be reverted in the app. The app identifies new "
                   + "mods on Nexus/CurseForge after its own drops; an agent's intake does not, so new rows may show "
                   + "no title until the user runs Identify my mods. Call list_mods to see the result.",
        };
    }

    private static IReadOnlyList<IntakeCollision> KeptCollisions(IntakePlan plan, ISet<string> replace)
        => plan.Collisions.Where(c => !replace.Contains(c.RelPath)).ToList();

    private static string LaneName(DropLane lane) => lane switch
    {
        DropLane.ModEngine2 => "mod_engine_2",
        DropLane.DirectInject => "direct_inject",
        _ => "mod_folder",
    };

    private static string RouteName(DropRoute r) => r switch
    {
        DropRoute.Framework => "framework",
        DropRoute.NotInstalled => "not_installed",
        DropRoute.SaveMod => "save_mod",
        DropRoute.Ue4ssLua => "ue4ss_lua",
        DropRoute.Tool => "tool",
        _ => "mod",
    };

    private static object Describe(string gameId, DropPlan plan, IReadOnlyList<string> paths, bool saveWritesNeedAck) => new
    {
        ok = true,
        gameId,
        blocked = plan.Blocked,
        needsBanRiskAcknowledgment = plan.NeedsBanRiskAcknowledgment,
        lane = LaneName(plan.Lane),
        missing = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToArray(),
        items = plan.Items.Select(i => new
        {
            path = i.Path,
            route = RouteName(i.Route),
            looksLikeFramework = i.LooksLikeFramework,
            framework = i.Framework is null ? null : new
            {
                id = i.Framework.FrameworkId,
                name = i.Framework.DisplayName,
                installRoot = i.FrameworkInstallRoot,
                files = i.FrameworkFiles?.Count ?? 0,
                overwrites = i.FrameworkOverwrites ?? Array.Empty<string>(),
            },
            saveMod = i.SaveMod is null ? null : new
            {
                worldId = i.SaveMod.WorldGuid,
                // What intake will do with it: a save mod on a game that asks before save writes is the
                // user's to install, in the app.
                outcome = i.SaveMod.Outcome != SaveModDropOutcome.NeedsAcknowledgment ? "refused"
                    : saveWritesNeedAck ? "needs_user" : "would_install",
            },
            luaModFolder = i.LuaModFolder,
            tool = i.Tool is null ? null : new { id = i.Tool.ToolId, name = i.Tool.DisplayName },
            problem = i.Problem,
        }).ToArray(),
        mods = new
        {
            problem = plan.IntakeProblem,
            toAdd = plan.Intake.ToAdd.Select(a => new { name = a.Name, relPath = a.RelPath, source = a.IncomingSource }).ToArray(),
            collisions = plan.Intake.Collisions.Select(c => new { name = c.Name, relPath = c.RelPath, existing = c.ExistingPath, source = c.IncomingSource }).ToArray(),
            refused = plan.Intake.Unsafe.Select(s => new { name = s.Name, reason = s.Reason }).ToArray(),
        },
        ue4ssModsDir = plan.Ue4ssModsDir,
        hint = plan.Blocked is not null ? "Nothing can be installed for this game until it has a mod folder."
            : plan.NeedsBanRiskAcknowledgment ? "intake will refuse this whole drop: the user must acknowledge the ban risk in the app first."
            : "intake installs this plan. Pass allowFrameworks for frameworks, continueAsMod for looks-like-framework "
              + "archives, and the relPaths in mods.collisions you want replaced. A save mod on a high-risk game "
              + "is installed only by the user, in the app.",
    };
}
