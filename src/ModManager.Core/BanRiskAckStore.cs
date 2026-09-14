using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// Persists the set of game ids for which the user has acknowledged the ban-risk warning, separated
/// by acknowledgment kind. EnableMods acks are stored in &lt;dataDir&gt;\ban-risk-acks.json (the original file,
/// unchanged for backward compatibility); WriteSaves acks are stored in &lt;dataDir&gt;\ban-risk-save-acks.json.
/// Both are plain JSON arrays of game ids. Tolerant by design: a missing or corrupt file yields an empty set,
/// never throws. Writes go through AtomicJson.
/// </summary>
public static class BanRiskAckStore
{
    private static string FileName(BanRiskAck kind) => kind switch
    {
        BanRiskAck.WriteSaves => "ban-risk-save-acks.json",
        _ => "ban-risk-acks.json", // EnableMods: the original file, unchanged so existing acks keep working
    };

    /// <summary>The acked game-id set for one kind. Missing or corrupt file -> empty.</summary>
    public static IReadOnlySet<string> Load(string dataDir, BanRiskAck kind = BanRiskAck.EnableMods)
    {
        var path = Path.Combine(dataDir, FileName(kind));
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            return ids is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(ids, StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public static bool IsAcked(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods)
        => !string.IsNullOrEmpty(gameId) && Load(dataDir, kind).Contains(gameId);

    /// <summary>Record an acknowledgment of one kind for a game and persist atomically. Idempotent.</summary>
    public static void Ack(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        var set = new HashSet<string>(Load(dataDir, kind), StringComparer.Ordinal) { gameId };
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, FileName(kind)), set.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }
}
