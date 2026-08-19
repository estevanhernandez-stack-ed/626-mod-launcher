using System.ComponentModel;
using ModelContextProtocol.Server;
using ModManager.Core;
using ModManager.Core.Agent;
using ModManager.Core.Persistence;

namespace ModManager.Mcp.Tools;

/// <summary>
/// The write half of the agent surface — E1, first slice.
///
/// <para><b>Everything here goes through the same Core paths the UI uses.</b> A toggle is
/// <c>Scanner.SetModEnabledAsync</c>, the same call a click makes, so the agent inherits reversibility
/// by construction rather than by promise: disabling MOVES files to the holding folder and nothing is
/// deleted. No file operation is re-implemented here, because a second implementation is a second set
/// of bugs and a second place for the laws to be forgotten.</para>
///
/// <para><b>The ban-risk gate refuses an agent outright.</b> There is no argument to pass, no dev flag,
/// no bypass. An agent must be able to REACH the gate and confirm it refuses, and must never be able
/// to SATISFY it — one that could tick the acknowledgement would have deleted the law rather than
/// honoured it. The refusal comes back as a CODE so the agent can tell its human what to do instead of
/// hunting for another route.</para>
///
/// <para><b>Every attempt is written down</b>, refusals included — see <see cref="AgentAudit"/>. A
/// log that recorded only successes would be silent about exactly the attempts a person needs to see.</para>
/// </summary>
[McpServerToolType]
public static class WriteTools
{
    [McpServerTool(Name = "set_mod_enabled")]
    [Description("Turn one mod on or off, through the same reversible path the app uses — disabling "
                 + "MOVES files to a holding folder and never deletes. Refuses outright on a high "
                 + "ban-risk game that the user has not acknowledged in the app; an agent cannot give "
                 + "that acknowledgement. Every call is recorded in agent-log.jsonl.")]
    public static async Task<object> SetModEnabled(
        [Description("The game id, from list_games.")] string gameId,
        [Description("The mod's name as list_mods reports it.")] string modName,
        [Description("True to turn it on, false to turn it off.")] bool enabled,
        [Description("Proceed even though another tool manages this mod's folder. Mirrors the app's "
                     + "own opt-in. Ignored when the folder is not managed.")] bool acknowledgeManaged = false)
    {
        var args = new Dictionary<string, string>
        {
            ["modName"] = modName,
            ["enabled"] = enabled.ToString(),
            ["acknowledgeManaged"] = acknowledgeManaged.ToString(),
        };

        var game = RegistryStore.Load(McpConfig.DataRoot).Games.FirstOrDefault(g => g.Id == gameId);
        if (game is null)
            return Refuse(null, gameId, args, AgentRefusal.NotFound, $"No game with id '{gameId}'.");

        var ctx = Scanner.GameContext(game);
        var mod = ModListing.Resolve(game).FirstOrDefault(m =>
            string.Equals(m.Name, modName, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
            return Refuse(ctx.DataDir, gameId, args, AgentRefusal.NotFound,
                $"'{modName}' is not a mod in {gameId}. Call list_mods for the names.");

        // Disabling is never gated: getting SAFER needs no friction, and that asymmetry is what makes
        // the ban-risk gate a safety feature rather than an obstacle.
        var decision = enabled
            ? AgentWriteRules.CanEnable(
                BanRiskCatalog.ByAppId(game.SteamAppId),
                BanRiskAckStore.IsAcked(ctx.DataDir, game.Id ?? ""),
                mod.ReadOnly,
                acknowledgeManaged)
            : AgentWriteRules.CanDisable();

        if (!decision.Allowed)
            return Refuse(ctx.DataDir, gameId, args, decision.Refusal, decision.Detail);

        try
        {
            // The same call the row toggle makes: DisableEntry moves to holding and refuses a
            // ReadOnly row, EnableMod restores by name, and a partial move rolls back.
            await Scanner.SetAppendedRowEnabledAsync(mod, enabled, ctx);
        }
        catch (Exception e)
        {
            AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(
                DateTime.UtcNow, "set_mod_enabled", gameId, args, "error", e.Message));
            return new { ok = false, refusal = "error", detail = e.Message };
        }

        AgentAudit.Append(ctx.DataDir, new AgentAuditEntry(
            DateTime.UtcNow, "set_mod_enabled", gameId, args, "ok",
            $"{modName} {(enabled ? "enabled" : "disabled")}."));

        return new
        {
            ok = true,
            gameId,
            modName = mod.Name,
            enabled,
            detail = enabled
                ? $"{mod.Name} is on."
                : $"{mod.Name} is off — its files moved to the holding folder and can be restored by turning it back on.",
            hint = "The app refreshes on its own if it is running. Call list_mods to see the new state.",
        };
    }

    [McpServerTool(Name = "get_agent_log")]
    [Description("What agents have done to this game, newest last: every write attempt including the "
                 + "ones that were refused, with the arguments they asked for.")]
    public static object GetAgentLog(
        [Description("The game id, from list_games.")] string gameId,
        [Description("How many entries to return, newest last. Default 50.")] int limit = 50)
    {
        var game = RegistryStore.Load(McpConfig.DataRoot).Games.FirstOrDefault(g => g.Id == gameId);
        if (game is null) return new { ok = false, detail = $"No game with id '{gameId}'." };

        var dataDir = Scanner.DataDirForGame(game);
        var all = AgentAudit.Read(dataDir);
        var take = Math.Clamp(limit, 1, 500);

        return new
        {
            gameId,
            path = AgentAudit.PathFor(dataDir),
            total = all.Count,
            entries = all.Skip(Math.Max(0, all.Count - take)).Select(e => new
            {
                utc = e.Utc,
                tool = e.Tool,
                args = e.Args,
                result = e.Result,
                detail = e.Detail,
            }),
            hint = "A refused entry is not a failure of the tool - it is the launcher declining, and "
                   + "the result field says which rule declined it.",
        };
    }

    // A refusal is a result, not an exception: the agent gets a code it can branch on, and the same
    // line lands in the audit trail so the user sees what was attempted.
    private static object Refuse(
        string? dataDir, string gameId, Dictionary<string, string> args, AgentRefusal refusal, string detail)
    {
        var code = ToCode(refusal);
        if (dataDir is not null)
            AgentAudit.Append(dataDir, new AgentAuditEntry(
                DateTime.UtcNow, "set_mod_enabled", gameId, args, code, detail));
        return new { ok = false, refusal = code, detail };
    }

    private static string ToCode(AgentRefusal r) => r switch
    {
        AgentRefusal.BanRiskNotAcknowledged => "ban_risk_not_acknowledged",
        AgentRefusal.ManagedByAnotherTool => "managed_by_another_tool",
        AgentRefusal.ConfirmationRequired => "confirmation_required",
        AgentRefusal.NotFound => "not_found",
        _ => "refused",
    };
}
