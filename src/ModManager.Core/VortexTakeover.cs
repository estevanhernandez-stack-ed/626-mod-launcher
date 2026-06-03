using System.Text.Json;

namespace ModManager.Core;

/// <summary>The persisted set of folders the user has taken over from another manager. Lives at
/// <c>&lt;dataDir&gt;/taken-over.json</c> (camelCase). Posture consults it so a taken-over folder reads
/// as not-owned even if a marker is still physically present.</summary>
public sealed class TakenOverState
{
    public int Version { get; set; } = 1;
    public List<string> Folders { get; set; } = new();
}

/// <summary>Read/write the taken-over set. camelCase via AtomicJson; case-insensitive on path.</summary>
public static class TakenOverStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "taken-over.json");

    /// <summary>The taken-over folders as a case-insensitive set. Missing/corrupt file -> empty.</summary>
    public static HashSet<string> Load(string dataDir)
    {
        try
        {
            var p = PathFor(dataDir);
            if (!File.Exists(p)) return new(StringComparer.OrdinalIgnoreCase);
            var state = JsonSerializer.Deserialize<TakenOverState>(File.ReadAllText(p), Json);
            return new HashSet<string>(state?.Folders ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    public static void Add(string dataDir, string folderAbs)
    {
        var set = Load(dataDir);
        if (!set.Add(folderAbs)) return;            // already present -> no rewrite
        Save(dataDir, set);
    }

    public static void Remove(string dataDir, string folderAbs)
    {
        var set = Load(dataDir);
        if (!set.Remove(folderAbs)) return;
        Save(dataDir, set);
    }

    private static void Save(string dataDir, HashSet<string> set)
        => AtomicJson.WriteJsonAtomic(PathFor(dataDir), new TakenOverState { Version = 1, Folders = set.ToList() });
}
