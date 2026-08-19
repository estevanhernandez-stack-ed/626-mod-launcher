using ModManager.Core;
using ModManager.Core.Agent;

namespace ModManager.Tests;

/// <summary>
/// E1, first slice. The rules between an agent and the file-touching paths.
///
/// <para><b>The load-bearing test in this file is
/// <see cref="An_agent_can_never_pass_the_ban_risk_gate"/>.</b> An agent must be able to REACH the gate
/// and confirm it refuses, and must never be able to SATISFY it. There is no argument, no flag and no
/// development posture that changes that answer — an agent that could tick the acknowledgement would
/// have deleted the law rather than honoured it.</para>
/// </summary>
public class AgentWriteRulesTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void An_agent_can_never_pass_the_ban_risk_gate(bool managed, bool acknowledgeManaged)
    {
        // Every combination of every other argument. None of them is a way through.
        var d = AgentWriteRules.CanEnable(GameBanRisk.High, acknowledged: false, managed, acknowledgeManaged);

        Assert.False(d.Allowed);
        Assert.Equal(AgentRefusal.BanRiskNotAcknowledged, d.Refusal);
    }

    [Fact]
    public void The_refusal_tells_the_agent_what_its_human_must_do()
    {
        // A code the agent can branch on, and a sentence it can relay. "Refused" with no route is how
        // an agent ends up retrying forever or inventing one.
        var d = AgentWriteRules.CanEnable(GameBanRisk.High, false, false, false);

        Assert.Contains("open the game in 626", d.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot acknowledge", d.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_human_acknowledgement_is_what_opens_it()
    {
        Assert.True(AgentWriteRules.CanEnable(GameBanRisk.High, acknowledged: true, false, false).Allowed);
    }

    [Theory]
    [InlineData(GameBanRisk.Medium)]
    [InlineData(GameBanRisk.Low)]
    [InlineData(GameBanRisk.None)]
    public void Only_HIGH_risk_gates_anything(GameBanRisk level)
    {
        // Medium is banner-only by design. Gating it would train people to click past the gate that
        // matters.
        Assert.True(AgentWriteRules.CanEnable(level, acknowledged: false, false, false).Allowed);
    }

    [Fact]
    public void Disabling_is_never_gated_at_all()
    {
        // Getting SAFER needs no friction. The asymmetry is what makes the gate a safety feature
        // rather than an obstacle, and it is stated rather than left as an absence.
        Assert.True(AgentWriteRules.CanDisable().Allowed);
    }

    [Fact]
    public void A_folder_another_tool_manages_is_refused_until_the_agent_says_so_explicitly()
    {
        var refused = AgentWriteRules.CanEnable(GameBanRisk.None, true, modIsManagedByAnotherTool: true,
            acknowledgeManaged: false);
        Assert.False(refused.Allowed);
        Assert.Equal(AgentRefusal.ManagedByAnotherTool, refused.Refusal);

        var allowed = AgentWriteRules.CanEnable(GameBanRisk.None, true, true, acknowledgeManaged: true);
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public void Ban_risk_is_checked_BEFORE_the_managed_folder_question()
    {
        // Order matters for the message. A user told "another tool manages this" would go and fix that,
        // and hit the real wall afterwards.
        var d = AgentWriteRules.CanEnable(GameBanRisk.High, false, modIsManagedByAnotherTool: true, false);

        Assert.Equal(AgentRefusal.BanRiskNotAcknowledged, d.Refusal);
    }

    [Fact]
    public void Anything_destructive_needs_an_explicit_confirm()
    {
        Assert.False(AgentWriteRules.CanDestroy(confirm: false).Allowed);
        Assert.Equal(AgentRefusal.ConfirmationRequired, AgentWriteRules.CanDestroy(false).Refusal);
        Assert.True(AgentWriteRules.CanDestroy(confirm: true).Allowed);
    }
}

/// <summary>
/// Law 8 of the agent-access sketch: <i>"A write surface with no record of what it did is not something
/// to hand someone else's game folder to."</i> It lands with the first write tool, not after it.
/// </summary>
public class AgentAuditTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agentlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static AgentAuditEntry Entry(string tool, string result) => new(
        DateTime.UtcNow, tool, "windrose",
        new Dictionary<string, string> { ["modName"] = "FasterShips10", ["enabled"] = "True" },
        result, "detail");

    [Fact]
    public void An_entry_round_trips_with_its_arguments_intact()
    {
        // What the agent ASKED for is the interesting half when a result surprises somebody.
        AgentAudit.Append(_dir, Entry("set_mod_enabled", "ok"));

        var read = Assert.Single(AgentAudit.Read(_dir));
        Assert.Equal("set_mod_enabled", read.Tool);
        Assert.Equal("windrose", read.GameId);
        Assert.Equal("FasterShips10", read.Args["modName"]);
        Assert.Equal("ok", read.Result);
    }

    [Fact]
    public void Refusals_are_recorded_too()
    {
        // A log of successes only would be silent about every attempt that mattered. "The agent tried
        // to enable a mod on a ban-risk game and was refused" is exactly what a user needs to see.
        AgentAudit.Append(_dir, Entry("set_mod_enabled", "ban_risk_not_acknowledged"));

        Assert.Equal("ban_risk_not_acknowledged", Assert.Single(AgentAudit.Read(_dir)).Result);
    }

    [Fact]
    public void The_trail_is_append_only_and_keeps_its_order()
    {
        foreach (var r in new[] { "ok", "not_found", "ok" }) AgentAudit.Append(_dir, Entry("set_mod_enabled", r));

        Assert.Equal(new[] { "ok", "not_found", "ok" }, AgentAudit.Read(_dir).Select(e => e.Result));
    }

    [Fact]
    public void It_is_camelCase_on_disk_like_everything_else_this_app_writes()
    {
        AgentAudit.Append(_dir, Entry("set_mod_enabled", "ok"));

        var raw = File.ReadAllText(AgentAudit.PathFor(_dir));
        Assert.Contains("\"tool\"", raw);
        Assert.Contains("\"gameId\"", raw);
        Assert.DoesNotContain("\"Tool\"", raw);
        Assert.DoesNotContain("\"GameId\"", raw);
    }

    [Fact]
    public void One_line_per_entry_so_it_can_be_tailed()
    {
        AgentAudit.Append(_dir, Entry("a", "ok"));
        AgentAudit.Append(_dir, Entry("b", "ok"));

        Assert.Equal(2, File.ReadAllLines(AgentAudit.PathFor(_dir)).Count(l => l.Trim().Length > 0));
    }

    [Fact]
    public void A_truncated_last_line_does_not_make_the_history_unreadable()
    {
        // A hard kill mid-write leaves half a line. Losing one entry is acceptable; losing the whole
        // trail because of it is not.
        AgentAudit.Append(_dir, Entry("set_mod_enabled", "ok"));
        File.AppendAllText(AgentAudit.PathFor(_dir), "{\"utc\":\"2026-08-18T00:00:00Z\",\"to");

        Assert.Single(AgentAudit.Read(_dir));
    }

    [Fact]
    public void Reading_a_trail_that_does_not_exist_yet_is_empty_not_an_error()
        => Assert.Empty(AgentAudit.Read(Path.Combine(_dir, "never-written")));

    [Fact]
    public void A_log_that_cannot_be_written_never_takes_down_the_operation()
    {
        // An audit failure must not break the thing it describes. A dropped line shows as a gap; a
        // dead tool does not.
        var ex = Record.Exception(() => AgentAudit.Append("\0:/nope", Entry("set_mod_enabled", "ok")));

        Assert.Null(ex);
    }
}
