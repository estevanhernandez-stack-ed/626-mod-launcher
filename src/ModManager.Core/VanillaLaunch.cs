using System.Text.Json;

namespace ModManager.Core;

/// <summary>The launch mode derived from on-disk state.</summary>
public enum LaunchMode { Modded, Vanilla }

/// <summary>One mod row that was active and got stepped aside (name + its mod location).</summary>
public sealed class StashedModRow
{
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
}

/// <summary>The exact set of loaders that were active and got stepped aside for a vanilla launch. Lives at
/// <c>&lt;dataDir&gt;/vanilla-stash.json</c> (camelCase). Restore replays EXACTLY this set — not "enable
/// all" — so a deliberately-off mod is never re-enabled.</summary>
public sealed class VanillaStash
{
    public int Version { get; set; } = 1;
    public DateTime SteppedAsideUtc { get; set; }
    public List<StashedModRow> ModRows { get; set; } = new();
    public List<string> Frameworks { get; set; } = new();
    public List<string> DirectInjectProxies { get; set; } = new();
}

/// <summary>Read/write the vanilla stash. camelCase via AtomicJson; missing/corrupt -> null.</summary>
public static class VanillaStashStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string PathFor(string dataDir) => Path.Combine(dataDir, "vanilla-stash.json");

    public static VanillaStash? Load(string dataDir)
    {
        try
        {
            var p = PathFor(dataDir);
            if (!File.Exists(p)) return null;
            return JsonSerializer.Deserialize<VanillaStash>(File.ReadAllText(p), Json);
        }
        catch { return null; }
    }

    public static void Save(string dataDir, VanillaStash stash) => AtomicJson.WriteJsonAtomic(PathFor(dataDir), stash);

    public static void Clear(string dataDir)
    {
        try { var p = PathFor(dataDir); if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
    }
}
