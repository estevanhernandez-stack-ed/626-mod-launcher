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

        // Resolve against the manifest's recorded install root, not gameRoot. Old manifests that
        // predate a reliable InstallPath fall back to gameRoot (the historic behavior).
        var installRoot = string.IsNullOrEmpty(m.InstallPath) ? gameRoot : m.InstallPath;

        // Delete every installed file. Idempotent — already-gone files are fine.
        foreach (var rel in m.InstalledFiles)
        {
            var abs = Path.Combine(installRoot, rel);
            try { if (File.Exists(abs)) File.Delete(abs); } catch { /* leave for manual */ }
        }

        // Restore the backup (if any) back over the install root.
        if (!string.IsNullOrEmpty(m.BackupSnapshotPath) && Directory.Exists(m.BackupSnapshotPath))
        {
            foreach (var src in Directory.EnumerateFiles(m.BackupSnapshotPath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(m.BackupSnapshotPath, src);
                var dst = Path.Combine(installRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }
        }

        // Tear down the framework dir — manifest + backup + any future per-framework state.
        try { Directory.Delete(fwDir, recursive: true); } catch { }
    }

    private const string DisabledProxyDir = "disabled-proxy";

    /// <summary>The top-level (no-slash) InstalledFiles entries — the loader proxy DLL(s) that make the
    /// framework inject (e.g. dwmapi.dll). Everything else lives under ue4ss/ and isn't a process hijack.</summary>
    private static IReadOnlyList<string> ProxyFiles(FrameworkInstallManifest m)
        => m.InstalledFiles.Where(f => !f.Replace('\\', '/').Contains('/')).ToList();

    private static FrameworkInstallManifest LoadManifest(string gameDataDir, string frameworkId)
    {
        var manifestPath = Path.Combine(gameDataDir, "frameworks", frameworkId, "install.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"No install manifest for framework '{frameworkId}'.", manifestPath);
        return JsonSerializer.Deserialize<FrameworkInstallManifest>(File.ReadAllText(manifestPath), Json)
               ?? throw new InvalidDataException($"Couldn't parse manifest for '{frameworkId}'.");
    }

    /// <summary>Reversibly disable an installed framework WITHOUT uninstalling it: move its loader proxy
    /// DLL(s) into a holding folder so the framework stops injecting, leaving the rest of the install
    /// (ue4ss/ + Mods) in place. Move-to-holding, never delete. No-op-safe when already disabled.</summary>
    public static void Disable(string gameDataDir, string frameworkId)
    {
        var m = LoadManifest(gameDataDir, frameworkId);
        var holding = Path.Combine(gameDataDir, "frameworks", frameworkId, DisabledProxyDir);
        Directory.CreateDirectory(holding);
        foreach (var rel in ProxyFiles(m))
        {
            var live = Path.Combine(m.InstallPath, rel);
            var held = Path.Combine(holding, rel);
            if (File.Exists(held)) continue;
            if (!File.Exists(live)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(held)!);
            File.Move(live, held);
        }
    }

    /// <summary>Re-enable a framework disabled via <see cref="Disable"/>: move its proxy DLL(s) back to the
    /// install root. Skips a proxy whose live name is already taken (a reinstalled copy is never clobbered).</summary>
    public static void Enable(string gameDataDir, string frameworkId)
    {
        var m = LoadManifest(gameDataDir, frameworkId);
        var holding = Path.Combine(gameDataDir, "frameworks", frameworkId, DisabledProxyDir);
        if (!Directory.Exists(holding)) return;
        foreach (var rel in ProxyFiles(m))
        {
            var held = Path.Combine(holding, rel);
            var live = Path.Combine(m.InstallPath, rel);
            if (!File.Exists(held) || File.Exists(live)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            File.Move(held, live);
        }
        try { Directory.Delete(holding, recursive: true); } catch { /* may hold un-restored entries */ }
    }

    /// <summary>True when the framework's proxy is currently stepped aside (holding has a proxy AND the
    /// live proxy is gone) — i.e. installed but not injecting.</summary>
    public static bool IsDisabled(string gameDataDir, string frameworkId)
    {
        FrameworkInstallManifest m;
        try { m = LoadManifest(gameDataDir, frameworkId); } catch { return false; }
        var holding = Path.Combine(gameDataDir, "frameworks", frameworkId, DisabledProxyDir);
        var proxies = ProxyFiles(m);
        if (proxies.Count == 0) return false;
        return proxies.Any(rel => File.Exists(Path.Combine(holding, rel)))
               && proxies.All(rel => !File.Exists(Path.Combine(m.InstallPath, rel)));
    }
}
