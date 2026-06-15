using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// Persists the set of game ids for which the user has acknowledged the ban-risk warning, at
/// &lt;dataDir&gt;\ban-risk-acks.json (a plain JSON array of game ids). Once a game is acked, the
/// enable gate stops prompting for it (the persistent banner still shows). Tolerant by design: a
/// missing or corrupt file yields an empty set, never throws. Writes go through AtomicJson.
/// </summary>
public static class BanRiskAckStore
{
    private const string FileName = "ban-risk-acks.json";

    /// <summary>The acked game-id set. Missing or corrupt file -> empty.</summary>
    public static IReadOnlySet<string> Load(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
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

    public static bool IsAcked(string dataDir, string gameId)
        => !string.IsNullOrEmpty(gameId) && Load(dataDir).Contains(gameId);

    /// <summary>Record an acknowledgment for a game and persist atomically. Idempotent.</summary>
    public static void Ack(string dataDir, string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        var set = new HashSet<string>(Load(dataDir), StringComparer.Ordinal) { gameId };
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, FileName), set.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }
}
