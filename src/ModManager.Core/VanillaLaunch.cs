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

/// <summary>The reversible mechanism operations VanillaLaunch composes, injected so Core stays testable
/// without a live game. The App wires real defaults (Scanner mod-row disable, FrameworkRegistry.Disable,
/// DirectInject proxy step-aside) + the active reads. Each "Active*" returns what is CURRENTLY loading.</summary>
public sealed class VanillaLaunchOps
{
    public required Func<IReadOnlyList<StashedModRow>> ActiveModRows { get; init; }
    public required Func<IReadOnlyList<string>> ActiveFrameworks { get; init; }
    public required Func<IReadOnlyList<string>> ActiveDirectInjectProxies { get; init; }
    public required Func<string, string, Task> DisableModRow { get; init; }   // (name, location)
    public required Func<string, string, Task> EnableModRow { get; init; }
    public required Action<string> DisableFramework { get; init; }
    public required Action<string> EnableFramework { get; init; }
    public required Action<string> DisableDirectInjectProxy { get; init; }
    public required Action<string> EnableDirectInjectProxy { get; init; }
}

/// <summary>Outcome of a StepAside/Restore.</summary>
public sealed record VanillaLaunchResult(bool Success, string? Error = null);

/// <summary>
/// Orchestrates a real vanilla launch: steps every active loader aside (mod rows + frameworks +
/// direct-inject proxies) as one reversible unit and records the EXACT set in vanilla-stash.json so
/// Restore replays only what was active. Composes the existing reversible primitives — no new file-op
/// law. Pure Core; the mechanism IO is injected via <see cref="VanillaLaunchOps"/>.
/// </summary>
public static class VanillaLaunch
{
    public static async Task<VanillaLaunchResult> StepAsideAsync(string dataDir, VanillaLaunchOps ops)
    {
        var rows = ops.ActiveModRows();
        var fws = ops.ActiveFrameworks();
        var proxies = ops.ActiveDirectInjectProxies();

        // Track what actually moved so a mid-step failure rolls back exactly those.
        var movedRows = new List<StashedModRow>();
        var movedFws = new List<string>();
        var movedProxies = new List<string>();
        try
        {
            foreach (var r in rows) { await ops.DisableModRow(r.Name, r.Location); movedRows.Add(r); }
            foreach (var id in fws) { ops.DisableFramework(id); movedFws.Add(id); }
            foreach (var p in proxies) { ops.DisableDirectInjectProxy(p); movedProxies.Add(p); }
        }
        catch (Exception ex)
        {
            foreach (var p in movedProxies) try { ops.EnableDirectInjectProxy(p); } catch { }
            foreach (var id in movedFws) try { ops.EnableFramework(id); } catch { }
            foreach (var r in movedRows) try { await ops.EnableModRow(r.Name, r.Location); } catch { }
            return new VanillaLaunchResult(false, ex.Message);
        }

        VanillaStashStore.Save(dataDir, new VanillaStash
        {
            Version = 1,
            SteppedAsideUtc = DateTime.UtcNow,
            ModRows = rows.ToList(),
            Frameworks = fws.ToList(),
            DirectInjectProxies = proxies.ToList(),
        });
        return new VanillaLaunchResult(true);
    }
}
