using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>A folder Vortex deployed, with its source archive name and the Nexus modId we parsed from it.</summary>
public sealed record VortexModRef(string Folder, string Source, int? NexusModId);

/// <summary>
/// Reads Vortex's deployment manifest (vortex.deployment.*.json) to recover, for each deployed
/// folder, the Nexus modId encoded in its source archive name — the only reliable handle on a
/// Vortex-extracted mod (its download archive is gone). Pure System.IO + regex.
/// </summary>
public static class VortexManifest
{
    // Nexus download name: "<name> <ver>-<modId>-<rest>-<timestamp>"; modId = first int group in the
    // trailing run, timestamp = trailing 9+ digits. Requires that trailing run to avoid false hits.
    private static readonly Regex ModIdRe = new(@"-(\d+)-.*-\d{9,}$", RegexOptions.Compiled);

    public static int? ParseNexusModId(string? source)
    {
        if (string.IsNullOrEmpty(source)) return null;
        var m = ModIdRe.Match(source);
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    public static IReadOnlyList<VortexModRef> Read(string modsDir)
    {
        string[] manifests;
        try { manifests = Directory.GetFiles(modsDir, "vortex.deployment.*.json"); }
        catch { return Array.Empty<VortexModRef>(); }

        var byFolder = new Dictionary<string, VortexModRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manifests)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("files", out var files)) continue;
                foreach (var el in files.EnumerateArray())
                {
                    var rel = el.TryGetProperty("relPath", out var rp) ? rp.GetString() : null;
                    var src = el.TryGetProperty("source", out var sp) ? sp.GetString() : null;
                    if (string.IsNullOrEmpty(rel) || string.IsNullOrEmpty(src)) continue;
                    var folder = rel.Replace('/', '\\').Split('\\')[0];
                    if (folder.Length == 0 || byFolder.ContainsKey(folder)) continue;
                    byFolder[folder] = new VortexModRef(folder, src!, ParseNexusModId(src));
                }
            }
            catch { /* skip a malformed manifest */ }
        }
        return byFolder.Values.ToList();
    }
}
