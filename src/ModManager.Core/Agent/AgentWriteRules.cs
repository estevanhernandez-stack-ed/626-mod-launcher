namespace ModManager.Core.Agent;

/// <summary>Why an agent write was refused. A CODE rather than a sentence, so the agent can branch on
/// it and report the right thing to its human instead of parsing prose.</summary>
public enum AgentRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>The game is high ban-risk and has not been acknowledged. There is NO agent path past
    /// this one — see <see cref="AgentWriteRules"/>.</summary>
    BanRiskNotAcknowledged,

    /// <summary>The mod's folder is managed by another tool. Passable, but only with an explicit
    /// acknowledgement, mirroring the checkbox the UI shows a person.</summary>
    ManagedByAnotherTool,

    /// <summary>A destructive operation without <c>confirm: true</c>.</summary>
    ConfirmationRequired,

    /// <summary>No such game, or no such mod in it.</summary>
    NotFound,
}

/// <summary>What an agent is allowed to do, and what it must never be allowed to do.</summary>
public sealed record AgentWriteDecision(bool Allowed, AgentRefusal Refusal, string Detail)
{
    public static AgentWriteDecision Ok() => new(true, AgentRefusal.None, "");
    public static AgentWriteDecision No(AgentRefusal r, string detail) => new(false, r, detail);
}

/// <summary>
/// The gate between an agent and the file-touching paths.
///
/// <para><b>The one rule with no development bypass: the ban-risk acknowledgement.</b> An agent must be
/// able to REACH the gate and confirm it refuses, and must never be able to SATISFY it. An agent that
/// can tick the acknowledgement has deleted the law rather than honoured it — the human acknowledges
/// in the app, or it does not happen.</para>
///
/// <para>That is why this returns a refusal CODE. The agent's correct behaviour on
/// <see cref="AgentRefusal.BanRiskNotAcknowledged"/> is to stop and tell its human what to do, not to
/// look for another route. Everything else here is friction with a documented way through, because
/// friction a caller can pass deliberately is a safety feature and friction it cannot pass is a wall.</para>
///
/// <para>Everything this permits still runs through the same Core paths the UI uses. The agent inherits
/// reversibility by construction rather than by promise: a toggle is still a move to the holding
/// folder, an intake is still no-clobber. Nothing here re-implements a file operation.</para>
/// </summary>
public static class AgentWriteRules
{
    /// <summary>May an agent enable this mod?</summary>
    /// <param name="banRisk">The game's resolved risk, live by Steam app id.</param>
    /// <param name="acknowledged">Whether the HUMAN has acknowledged it, from the on-disk ack.</param>
    /// <param name="modIsManagedByAnotherTool">Whether the mod's folder is owned elsewhere.</param>
    /// <param name="acknowledgeManaged">The agent's explicit opt-in for the owned-folder case.</param>
    public static AgentWriteDecision CanEnable(
        GameBanRisk banRisk, bool acknowledged, bool modIsManagedByAnotherTool, bool acknowledgeManaged)
    {
        // First and unconditionally. Enabling is the act that carries the risk, and no argument the
        // agent can pass changes this answer.
        if (BanRiskRules.ShouldGateEnable(banRisk, acknowledged))
            return AgentWriteDecision.No(AgentRefusal.BanRiskNotAcknowledged,
                "This game uses anti-cheat and the risk has not been acknowledged. An agent cannot "
                + "acknowledge it — open the game in 626 and enable a mod there once, and the "
                + "acknowledgement carries from then on.");

        if (modIsManagedByAnotherTool && !acknowledgeManaged)
            return AgentWriteDecision.No(AgentRefusal.ManagedByAnotherTool,
                "Another tool manages this mod's folder. Pass acknowledgeManaged to proceed anyway, "
                + "which is the same choice the app offers a person.");

        return AgentWriteDecision.Ok();
    }

    /// <summary>Disabling is never gated. Getting SAFER needs no friction, and the same asymmetry the
    /// UI has — warn on the way in, never on the way out — is what makes the gate a safety feature
    /// rather than an obstacle. Kept as a named method so the asymmetry is stated rather than implied
    /// by an absence.</summary>
    public static AgentWriteDecision CanDisable() => AgentWriteDecision.Ok();

    /// <summary>Anything that deletes needs an explicit confirm, and no bulk form of it exists. An
    /// agent removing twenty mods loops twenty confirms; the friction is the point.</summary>
    public static AgentWriteDecision CanDestroy(bool confirm)
        => confirm
            ? AgentWriteDecision.Ok()
            : AgentWriteDecision.No(AgentRefusal.ConfirmationRequired,
                "This permanently deletes files. Pass confirm to proceed.");
}
