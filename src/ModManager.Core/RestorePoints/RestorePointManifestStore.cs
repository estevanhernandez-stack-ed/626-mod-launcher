using System.Text.Json;

namespace ModManager.Core.RestorePoints;

/// <summary>Reads/writes the single <c>manifest.json</c> at the root of a restore point. Writes go
/// through <see cref="AtomicJson"/> (atomic temp+rename, camelCase). Reads tolerate either casing.</summary>
public static class RestorePointManifestStore
{
    public const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Write the manifest as the SEAL — call this LAST, after all payload is captured + verified.</summary>
    public static void WriteSealed(string restorePointDir, RestorePointManifest manifest)
        => AtomicJson.WriteJsonAtomic(Path.Combine(restorePointDir, FileName), manifest);

    /// <summary>Read the manifest, or null if there is no manifest file (an unsealed/partial point).</summary>
    public static RestorePointManifest? Read(string restorePointDir)
    {
        var path = Path.Combine(restorePointDir, FileName);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<RestorePointManifest>(File.ReadAllText(path), ReadOpts);
    }

    public sealed record Validation(bool Ok, string? Reason);

    /// <summary>Refuse a manifest that is missing the seal or was written by a newer build.</summary>
    public static Validation Validate(RestorePointManifest? m, int supportedSchema)
    {
        if (m is null) return new Validation(false, "No manifest found — the restore point is incomplete or missing.");
        if (!m.Complete) return new Validation(false, "Restore point is incomplete (the Safe Clear didn't finish sealing it).");
        if (m.SchemaVersion > supportedSchema)
            return new Validation(false, $"Restore point uses a newer format (schema {m.SchemaVersion} > supported {supportedSchema}) — update the launcher.");
        return new Validation(true, null);
    }
}
