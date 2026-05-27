using System.Text.Json;

namespace ModManager.Core.Frameworks;

/// <summary>
/// Read + maintain the on-disk record of installed frameworks under
/// <c>&lt;gameData&gt;/frameworks/&lt;frameworkId&gt;/install.json</c>. Settings → Installed
/// frameworks reads via <see cref="List"/>; the uninstall button calls <see cref="Uninstall"/>.
/// </summary>
public static class FrameworkRegistry
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<FrameworkInstallManifest> List(string gameDataDir)
    {
        var root = Path.Combine(gameDataDir, "frameworks");
        if (!Directory.Exists(root)) return Array.Empty<FrameworkInstallManifest>();

        var manifests = new List<FrameworkInstallManifest>();
        foreach (var fwDir in Directory.EnumerateDirectories(root))
        {
            var path = Path.Combine(fwDir, "install.json");
            if (!File.Exists(path)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<FrameworkInstallManifest>(File.ReadAllText(path), Json);
                if (m is not null) manifests.Add(m);
            }
            catch { /* ignore unreadable manifests — surface in a later log pass */ }
        }
        return manifests;
    }

    /// <summary>
    /// Reverse a framework install. Deletes every installed file, restores any pre-install
    /// backup snapshot (if present), and tears down the framework's data subfolder.
    /// Idempotent against partial state — a missing file mid-uninstall doesn't abort the
    /// rest of the cleanup.
    /// </summary>
    public static void Uninstall(string gameDataDir, string frameworkId, string gameRoot)
    {
        var fwDir = Path.Combine(gameDataDir, "frameworks", frameworkId);
        var manifestPath = Path.Combine(fwDir, "install.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException(
                $"No install manifest for framework '{frameworkId}'.", manifestPath);

        var m = JsonSerializer.Deserialize<FrameworkInstallManifest>(File.ReadAllText(manifestPath), Json)
                ?? throw new InvalidDataException($"Couldn't parse manifest for '{frameworkId}'.");

        // Delete every installed file. Idempotent — already-gone files are fine.
        foreach (var rel in m.InstalledFiles)
        {
            var abs = Path.Combine(gameRoot, rel);
            try { if (File.Exists(abs)) File.Delete(abs); } catch { /* leave for manual */ }
        }

        // Restore the backup (if any) — copy each file from the backup tree back over the
        // install root. This puts back the original files the install replaced.
        if (!string.IsNullOrEmpty(m.BackupSnapshotPath) && Directory.Exists(m.BackupSnapshotPath))
        {
            foreach (var src in Directory.EnumerateFiles(m.BackupSnapshotPath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(m.BackupSnapshotPath, src);
                var dst = Path.Combine(gameRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
        }

        // Tear down the framework dir — manifest + backup + any future per-framework state.
        try { Directory.Delete(fwDir, recursive: true); } catch { }
    }
}
