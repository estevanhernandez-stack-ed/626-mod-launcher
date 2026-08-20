using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// The names a player gives their worlds.
///
/// <para><b>Ours, not the game's — and that is the point.</b> Palworld names a world by its folder
/// GUID: <c>905979404BC61E4EF56946B155337D7F</c> means nothing to anybody. The real name lives inside
/// the save, behind a <c>PlM1</c> container and a GVAS tree, in a game that patches often. Reading it
/// would make every panel that shows a world name depend on somebody else's reverse-engineering of a
/// moving format, to render a label.</para>
///
/// <para>So the label is one the player typed, kept beside our own data. No parser, nothing to break
/// when the save format shifts, and the name is theirs rather than one we guessed. It costs a rename
/// the first time, and buys a panel where <i>"Replace Ridgeline Base with the backup from Tuesday"</i>
/// is a sentence someone can act on.</para>
///
/// <para>Keyed on the folder name, which survives a restore — so a label outlives the operations that
/// change what is inside the world.</para>
/// </summary>
public sealed record WorldLabels(IReadOnlyDictionary<string, string> ByWorldId)
{
    public const string FileName = "world-labels.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static WorldLabels Empty => new(new Dictionary<string, string>());

    private static string PathFor(string gameDataDir) => Path.Combine(gameDataDir, FileName);

    /// <summary>Never throws. A missing, empty, or malformed file is "no labels yet" — the panel falls
    /// back to ordinals, which is a worse label but never a crash and never a lost world.</summary>
    public static WorldLabels Load(string gameDataDir)
    {
        try
        {
            var p = PathFor(gameDataDir);
            if (!File.Exists(p)) return Empty;
            var text = File.ReadAllText(p);
            if (string.IsNullOrWhiteSpace(text)) return Empty;
            return JsonSerializer.Deserialize<WorldLabels>(text, Json) ?? Empty;
        }
        catch { return Empty; }
    }

    public static void Save(string gameDataDir, WorldLabels labels)
        => AtomicJson.WriteJsonAtomic(PathFor(gameDataDir), labels);

    /// <summary>The label for a world, or null when it has never been named.</summary>
    public string? For(string worldId)
        => ByWorldId.TryGetValue(worldId, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>
    /// Set or clear a label. A blank name clears it rather than storing whitespace.
    ///
    /// <para>Clearing removes the key; setting keeps every other entry, <b>including labels for worlds
    /// that are not on disk right now</b>. That is deliberate: a world can come back from a snapshot
    /// under the same id, and silently forgetting the one thing the user typed would be a small
    /// betrayal at exactly the moment they are recovering from something.</para>
    /// </summary>
    public WorldLabels With(string worldId, string? label)
    {
        var next = new Dictionary<string, string>(ByWorldId, StringComparer.Ordinal);
        var clean = (label ?? "").Trim();
        if (clean.Length == 0) next.Remove(worldId);
        else next[worldId] = clean;
        return new WorldLabels(next);
    }

    /// <summary>What to show for a world: its label if it has one, else a positional fallback so the
    /// panel is never blank.</summary>
    public string Display(string worldId, int ordinal) => For(worldId) ?? $"World {ordinal}";
}
