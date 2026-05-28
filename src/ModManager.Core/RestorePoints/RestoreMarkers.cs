using System.Text.Json;

namespace ModManager.Core.RestorePoints;

/// <summary>Small camelCase JSON markers. RESTORE-AVAILABLE.json is left in a per-game data dir so a
/// fresh re-add of the same (deterministic-slug) game detects an archived setup — the game-id hook.
/// last-clear.json sits under the app data root so Phase 2 onboarding can offer "Restore a previous
/// setup" after a clear. Pure, atomic via AtomicJson.</summary>
public static class RestoreMarkers
{
    public const string RestoreAvailableFile = "RESTORE-AVAILABLE.json";
    public const string LastClearFile = "last-clear.json";

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record RestoreAvailable(string RestorePoint);
    public sealed record LastClear(string ClearedUtc, string RestorePoint);

    public static void WriteRestoreAvailable(string dataDir, string restorePointTimestamp)
        => AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, RestoreAvailableFile), new RestoreAvailable(restorePointTimestamp));

    public static string? ReadRestoreAvailable(string dataDir)
    {
        var p = Path.Combine(dataDir, RestoreAvailableFile);
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<RestoreAvailable>(File.ReadAllText(p), ReadOpts)?.RestorePoint;
    }

    public static void WriteLastClear(string appDataRoot, string clearedUtc, string restorePointTimestamp)
        => AtomicJson.WriteJsonAtomic(Path.Combine(appDataRoot, LastClearFile), new LastClear(clearedUtc, restorePointTimestamp));

    public static LastClear? ReadLastClear(string appDataRoot)
    {
        var p = Path.Combine(appDataRoot, LastClearFile);
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<LastClear>(File.ReadAllText(p), ReadOpts);
    }

    public static void ClearLastClear(string appDataRoot)
    {
        var p = Path.Combine(appDataRoot, LastClearFile);
        try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
    }
}
