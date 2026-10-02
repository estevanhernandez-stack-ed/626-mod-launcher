using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Persistence;

namespace ModManager.Mcp.Tools;

/// <summary>
/// Loadouts (saved profiles) for an agent — E1, second slice. Read what exists; apply one through the
/// same Core path the app's Profiles dialog uses (<see cref="Scanner.LoadProfileAsync"/>, which routes
/// every change through the one toggle router), behind the same ban-risk law as <c>set_mod_enabled</c>.
/// </summary>
[McpServerToolType]
public static class ProfileTools
{
    [McpServerTool(Name = "list_profiles")]
    [Description("The saved loadouts (profiles) for a game, by name. Each is a snapshot of which mods "
                 + "were on and off when it was saved.")]
    public static async Task<object> ListProfiles(
        [Description("The game id, from list_games.")] string gameId)
    {
        var game = RegistryStore.Load(McpConfig.DataRoot).Games.FirstOrDefault(g => g.Id == gameId);
        if (game is null) return new { ok = false, detail = $"No game with id '{gameId}'." };

        var names = await Scanner.ListProfilesAsync(Scanner.GameContext(game));
        return new { ok = true, gameId, profiles = names };
    }

    [McpServerTool(Name = "apply_loadout")]
    [Description("Apply a saved loadout: turn each mod it names on or off to match, through the same "
                 + "reversible path the app uses (disabling MOVES files to a holding folder, never deletes). "
                 + "Mods another tool manages are left alone. Refuses the WHOLE loadout, changing nothing, "
                 + "when it would turn anything on in a high ban-risk game the user has not acknowledged "
                 + "in the app. Pass dryRun to see the changes without making them. Every call is recorded "
                 + "in agent-log.jsonl.")]
    public static async Task<object> ApplyLoadout(
        [Description("The game id, from list_games.")] string gameId,
        [Description("The loadout's name, from list_profiles.")] string profileName,
        [Description("List what would change and change nothing.")] bool dryRun = false)
    {
        const string tool = "apply_loadout";
        var args = new Dictionary<string, string> { ["profileName"] = profileName, ["dryRun"] = dryRun.ToString() };

        var game = RegistryStore.Load(McpConfig.DataRoot).Games.FirstOrDefault(g => g.Id == gameId);
        if (game is null)
            return WriteTools.Refuse(tool, null, gameId, args, AgentRefusal.NotFound, $"No game with id '{gameId}'.");

        var ctx = Scanner.GameContext(game);
        // The ONE plan this call gates, applies and verifies. Re-planning inside the apply would run
        // whatever the file or the mods say by then, past a gate that was asked about something else.
        IReadOnlyList<(Mod Mod, bool Enable)> plan;
        try { plan = Scanner.ProfilePlan(profileName, ctx); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, AgentRefusal.NotFound,
                $"No loadout named '{profileName}' for {gameId}. Call list_profiles for the names.");
        }
        catch (Exception e)
        {
            // Truncated JSON, a locked or unreadable file: still a recorded answer, never an escape that
            // leaves no line in the log.
            var detail = $"Couldn't read the loadout '{profileName}': {e.Message}";
            AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "unreadable", detail));
            return new { ok = false, refusal = "unreadable", detail };
        }

        var changes = plan.Select(p => new { modName = p.Mod.Name, enable = p.Enable }).ToList();

        // One decision for the whole loadout, before anything moves: a loadout half-applied because the
        // gate stopped it partway would be a state nobody chose. Disabling alone is never gated.
        var decision = plan.Any(p => p.Enable)
            ? AgentWriteRules.CanEnable(
                BanRiskCatalog.Effective(game),
                BanRiskAckStore.IsAcked(ctx.DataDir, game.Id ?? ""),
                modIsManagedByAnotherTool: false,   // the plan already leaves those mods alone
                acknowledgeManaged: false)
            : AgentWriteRules.CanDisable();

        // A dry run changes nothing, so it answers even where applying would be refused, and says so:
        // seeing what a loadout WOULD do is how an agent explains the refusal to its human.
        if (dryRun)
        {
            var wouldRefuse = decision.Allowed ? null : WriteTools.ToCode(decision.Refusal);
            AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(
                DateTime.UtcNow, tool, gameId, args, "dry_run",
                $"{plan.Count} change(s) planned, none made{(wouldRefuse is null ? "" : $"; applying would be refused ({wouldRefuse})")}."));
            return new
            {
                ok = true, gameId, profileName, dryRun = true, changes,
                wouldRefuse,
                detail = decision.Allowed ? null : decision.Detail,
            };
        }

        if (!decision.Allowed)
            return WriteTools.Refuse(tool, ctx.DataDir, gameId, args, decision.Refusal, decision.Detail);

        // The plan that was gated is the plan that runs. Every change is tried; one that throws does not
        // strand the rest, and the answer names what did and did not land.
        var failed = await Scanner.ApplyProfilePlanAsync(plan, ctx);

        // Check the whole plan against ONE listing instead of assuming it took, as set_mod_enabled does.
        var notApplied = ModToggle.NotApplied(game, plan.Select(p => (p.Mod.Name, p.Enable)));
        if (failed.Count > 0 || notApplied.Count > 0)
        {
            var errors = failed.Select(f => new { modName = f.Mod.Name, error = f.Error }).ToList();
            var detail = $"Applied {plan.Count - notApplied.Count} of {plan.Count} change(s); the mod list does "
                         + $"not show these as asked: {string.Join(", ", notApplied)}."
                         + (failed.Count > 0 ? $" {failed.Count} of them failed with an error." : "");
            AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(DateTime.UtcNow, tool, gameId, args, "not_applied", detail));
            return new { ok = false, refusal = "not_applied", detail, notApplied, errors, changes };
        }

        AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(
            DateTime.UtcNow, tool, gameId, args, "ok", $"Applied '{profileName}': {plan.Count} change(s)."));
        return new
        {
            ok = true,
            gameId,
            profileName,
            changes,
            detail = plan.Count == 0
                ? "Nothing to change: the mods already match this loadout."
                : $"Applied '{profileName}': {plan.Count} change(s). Turned-off mods' files moved to the holding folder.",
            hint = "The app refreshes on its own if it is running. Call list_mods to see the new state.",
        };
    }
}
