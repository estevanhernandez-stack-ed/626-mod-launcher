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

    internal static string PathFor(string dataDir) => Path.Combine(dataDir, "vanilla-stash.json");

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
    /// <summary>(name, location) → true when the row actually moved; false when its lane skipped it.</summary>
    public required Func<string, string, Task<bool>> DisableModRow { get; init; }
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
        IReadOnlyList<string> proxies = Array.Empty<string>();

        // Track what actually moved so a mid-step failure rolls back exactly those, and the stash records only
        // them. A row the lane skipped (already off on disk, the game's own file, gone) is not recorded: Restore
        // would otherwise turn on a mod that was off before vanilla.
        var movedRows = new List<StashedModRow>();
        var movedFws = new List<string>();
        var movedProxies = new List<string>();
        try
        {
            foreach (var r in rows) { if (await ops.DisableModRow(r.Name, r.Location)) movedRows.Add(r); }
            foreach (var id in fws) { ops.DisableFramework(id); movedFws.Add(id); }
            // Read the proxies AFTER the rows: a loader that is also a row (FromSoft's dinput8) has already gone
            // to its row's holding. Recording it here too made Restore pull from both, and a stale copy in the
            // proxy holding could come back live while the row's restore then refused the taken name.
            proxies = ops.ActiveDirectInjectProxies();
            foreach (var p in proxies) { ops.DisableDirectInjectProxy(p); movedProxies.Add(p); }

            // The stash write is INSIDE the rollback region: if it fails (disk full, etc.) we must put
            // every loader back, or they're stranded — stepped aside on disk with no stash to restore
            // from. A stash-write failure therefore rolls back the moves like any step-aside failure.
            VanillaStashStore.Save(dataDir, new VanillaStash
            {
                Version = 1,
                SteppedAsideUtc = DateTime.UtcNow,
                ModRows = movedRows.ToList(),
                Frameworks = fws.ToList(),
                DirectInjectProxies = proxies.ToList(),
            });
        }
        catch (Exception ex)
        {
            var stuck = new List<string>();
            foreach (var p in movedProxies) try { ops.EnableDirectInjectProxy(p); } catch { stuck.Add(p); }
            foreach (var id in movedFws) try { ops.EnableFramework(id); } catch { stuck.Add(id); }
            foreach (var r in movedRows) try { await ops.EnableModRow(r.Name, r.Location); } catch { stuck.Add(r.Name); }
            return new VanillaLaunchResult(false, stuck.Count == 0 ? ex.Message
                : $"{ex.Message} These didn't go back and are still set aside: {string.Join(", ", stuck)}.");
        }

        return new VanillaLaunchResult(true);
    }

    public static async Task<VanillaLaunchResult> RestoreAsync(string dataDir, VanillaLaunchOps ops)
    {
        var stash = VanillaStashStore.Load(dataDir);
        if (stash is null) return new VanillaLaunchResult(true); // nothing stepped aside — no-op

        try
        {
            // Restore in reverse order of step-aside: proxies + frameworks first (so the loader is back
            // before its mods), then the mod rows. Each Enable is itself no-clobber/idempotent.
            foreach (var p in stash.DirectInjectProxies) ops.EnableDirectInjectProxy(p);
            foreach (var id in stash.Frameworks) ops.EnableFramework(id);
        }
        catch (Exception ex)
        {
            return new VanillaLaunchResult(false, ex.Message); // leave the stash so a retry can finish
        }

        // Rows one at a time: one refusal (a held copy colliding with a fresh install) must not strand every row
        // after it. The stash keeps only the rows that didn't come back, so a retry finishes exactly those.
        var failed = new List<(StashedModRow Row, string Error)>();
        foreach (var r in stash.ModRows)
        {
            try { await ops.EnableModRow(r.Name, r.Location); }
            catch (Exception ex) { failed.Add((r, ex.Message)); }
        }
        if (failed.Count > 0)
        {
            VanillaStashStore.Save(dataDir, new VanillaStash
            {
                Version = stash.Version,
                SteppedAsideUtc = stash.SteppedAsideUtc,
                ModRows = failed.Select(f => f.Row).ToList(),
            });
            return new VanillaLaunchResult(false,
                string.Join(" ", failed.Select(f => $"{f.Row.Name}: {f.Error.Trim().TrimEnd('.')}.")));
        }

        VanillaStashStore.Clear(dataDir);
        return new VanillaLaunchResult(true);
    }

    /// <summary>Vanilla when a stash exists (we stepped aside), Modded otherwise. Drives the button label.</summary>
    public static LaunchMode CurrentMode(string dataDir)
        => VanillaStashStore.Load(dataDir) is not null ? LaunchMode.Vanilla : LaunchMode.Modded;
}
