using System.Text.Json;

namespace ModManager.Core.Agent;

/// <summary>One line of the agent audit trail. camelCase on disk like everything else the launcher
/// writes.</summary>
/// <param name="Utc">When it happened.</param>
/// <param name="Tool">The MCP tool name, e.g. <c>set_mod_enabled</c>.</param>
/// <param name="GameId">Which game it touched, when it touched one.</param>
/// <param name="Args">The arguments as the agent supplied them — what it ASKED for, which is the
/// interesting half when a result surprises somebody.</param>
/// <param name="Result">What happened: <c>ok</c>, or a refusal code.</param>
/// <param name="Detail">One sentence a person can read.</param>
public sealed record AgentAuditEntry(
    DateTime Utc,
    string Tool,
    string? GameId,
    IReadOnlyDictionary<string, string> Args,
    string Result,
    string Detail);

/// <summary>
/// Every write an agent makes, written down.
///
/// <para><b>Law 8 of the agent-access sketch, and it lands with the FIRST write tool rather than
/// after it:</b> <i>"A write surface with no record of what it did is not something to hand someone
/// else's game folder to."</i> An agent acting on a library is only acceptable if the person who owns
/// the library can see exactly what it did, in order, afterwards.</para>
///
/// <para>Append-only JSONL at <c>&lt;dataRoot&gt;/agent-log.jsonl</c>. One line per operation, flushed
/// on write — a crash mid-session must not lose the record of what was already done, which is the one
/// circumstance where the log matters most.</para>
///
/// <para><b>Refusals are logged too, and that is deliberate.</b> "The agent tried to enable a mod on a
/// ban-risk game and was refused" is exactly the sentence a user needs to see, and a log that only
/// records successes would be silent about every attempt that mattered.</para>
/// </summary>
public static class AgentAudit
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,   // one line per entry: JSONL, tail-able, appendable
    };

    private static readonly object Gate = new();

    public static string PathFor(string dataRoot) => Path.Combine(dataRoot, "agent-log.jsonl");

    /// <summary>Append one entry. Never throws: an audit failure must not take down the operation it
    /// is describing, and a tool that refuses to work because it could not write its own log is worse
    /// than one that works loudly. A dropped line is visible as a gap; a dead tool is not.</summary>
    public static void Append(string dataRoot, AgentAuditEntry entry)
    {
        try
        {
            Directory.CreateDirectory(dataRoot);
            var line = JsonSerializer.Serialize(entry, JsonOpts);
            lock (Gate) File.AppendAllText(PathFor(dataRoot), line + Environment.NewLine);
        }
        catch { /* see summary */ }
    }

    /// <summary>Read the trail back, oldest first. A malformed line is SKIPPED rather than fatal — a
    /// truncated last line from a hard kill must not make the whole history unreadable.</summary>
    public static IReadOnlyList<AgentAuditEntry> Read(string dataRoot)
    {
        var path = PathFor(dataRoot);
        var entries = new List<AgentAuditEntry>();
        if (!File.Exists(path)) return entries;
        foreach (var line in SafeLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var e = JsonSerializer.Deserialize<AgentAuditEntry>(line, JsonOpts);
                if (e is not null) entries.Add(e);
            }
            catch { }
        }
        return entries;
    }

    private static IEnumerable<string> SafeLines(string path)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { yield break; }
        foreach (var l in lines) yield return l;
    }
}
