using System.IO.Compression;
using System.Text.Json;

namespace ModManager.Core.Frameworks;

/// <summary>
/// The persisted record of a framework install. Lives at
/// <c>&lt;gameData&gt;/frameworks/&lt;frameworkId&gt;/install.json</c>. camelCase JSON shape
/// matches the Electron-shared state-file convention from the legacy launcher.
/// </summary>
public sealed record FrameworkInstallManifest(
    string FrameworkId,
    string DisplayName,
    string Author,
    string InstallPath,
    IReadOnlyList<string> InstalledFiles,
    DateTime InstalledUtc,
    string? BackupSnapshotPath);

/// <summary>Result returned to the App layer after a successful install.</summary>
public sealed record FrameworkInstallResult(
    string FrameworkId,
    string InstallPath,
    IReadOnlyList<string> InstalledFiles,
    DateTime InstalledUtc,
    string? BackupSnapshotPath);

/// <summary>
/// Pure-core installer for catalog-known frameworks. Backs up any file it's about to
/// overwrite (so uninstall is reversible), then extracts the archive to the framework's
/// install root, then writes the manifest. Forbidden paths (declared by the catalog entry)
/// abort the entire install before any file is touched — directory-traversal entries (..
/// resolving outside install root) too.
///
/// NO Electron, NO WinUI. System.IO + System.IO.Compression + System.Text.Json only —
/// same dep surface as the rest of Core.
/// </summary>
public static class FrameworkInstaller
{
    public static FrameworkInstallResult Install(
        string archivePath, KnownFramework framework, string gameRoot, string gameDataDir)
    {
        if (string.IsNullOrEmpty(archivePath)) throw new ArgumentException("archivePath empty", nameof(archivePath));
        if (framework is null) throw new ArgumentNullException(nameof(framework));
        if (string.IsNullOrEmpty(gameRoot)) throw new ArgumentException("gameRoot empty", nameof(gameRoot));
        if (string.IsNullOrEmpty(gameDataDir)) throw new ArgumentException("gameDataDir empty", nameof(gameDataDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException("Archive missing.", archivePath);

        string installRoot = framework.InstallRoot switch
        {
            "GameRoot" => gameRoot,
            _ => throw new InvalidOperationException(
                $"Unknown framework install root '{framework.InstallRoot}'."),
        };

        var frameworkDir = Path.Combine(gameDataDir, "frameworks", framework.FrameworkId);
        var backupRoot = Path.Combine(frameworkDir, "backup", DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        var installRootFull = Path.GetFullPath(installRoot);

        using var zip = ZipFile.OpenRead(archivePath);

        // 1) Validate every entry path BEFORE touching disk. Reject directory traversal +
        //    forbidden paths up front so partial-extract states are impossible.
        var plannedEntries = new List<(ZipArchiveEntry Entry, string RelativeNorm, string AbsTarget)>();
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName)) continue;
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;  // directory marker

            var relNorm = entry.FullName.Replace('\\', '/');
            var absTarget = Path.GetFullPath(Path.Combine(installRoot, relNorm));

            // Containment check — refuse anything that resolves outside install root.
            bool inside = absTarget.StartsWith(installRootFull + Path.DirectorySeparatorChar,
                              StringComparison.OrdinalIgnoreCase)
                          || string.Equals(absTarget, installRootFull, StringComparison.OrdinalIgnoreCase);
            if (!inside)
            {
                throw new InvalidOperationException(
                    $"Archive entry '{entry.FullName}' resolves outside the install root — refusing install.");
            }

            // Forbidden-paths gate — basename OR relative-path equality.
            if (framework.ForbiddenPaths.Any(forbidden =>
                string.Equals(Path.GetFileName(relNorm), forbidden, StringComparison.OrdinalIgnoreCase)
                || string.Equals(relNorm, forbidden, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Archive contains a forbidden path '{entry.FullName}' — refusing install. " +
                    $"Frameworks must never overwrite the game's protected files.");
            }

            plannedEntries.Add((entry, relNorm, absTarget));
        }

        // 2) Back up existing files that will be overwritten. Each backup goes under a
        //    timestamped folder so multiple installs don't clobber each other.
        string? createdBackupRoot = null;
        foreach (var (_, relNorm, absTarget) in plannedEntries)
        {
            if (!File.Exists(absTarget)) continue;
            createdBackupRoot ??= backupRoot;
            var backupPath = Path.Combine(backupRoot, relNorm);
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(absTarget, backupPath, overwrite: false);
        }

        // 3) Extract. Validation passed up front — partial-extract states are impossible.
        var installed = new List<string>(plannedEntries.Count);
        foreach (var (entry, relNorm, absTarget) in plannedEntries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absTarget)!);
            using (var src = entry.Open())
            using (var dst = File.Create(absTarget))
                src.CopyTo(dst);
            installed.Add(relNorm);
        }

        // 4) Write the manifest. Atomic temp + rename mirrors FsAtomic.WriteJsonAtomic.
        var manifest = new FrameworkInstallManifest(
            FrameworkId: framework.FrameworkId,
            DisplayName: framework.DisplayName,
            Author: framework.Author,
            InstallPath: installRoot,
            InstalledFiles: installed,
            InstalledUtc: DateTime.UtcNow,
            BackupSnapshotPath: createdBackupRoot);

        Directory.CreateDirectory(frameworkDir);
        var manifestPath = Path.Combine(frameworkDir, "install.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        var tempPath = manifestPath + ".tmp";
        File.WriteAllText(tempPath, json);
        if (File.Exists(manifestPath)) File.Delete(manifestPath);
        File.Move(tempPath, manifestPath);

        return new FrameworkInstallResult(
            framework.FrameworkId, installRoot, installed, manifest.InstalledUtc, createdBackupRoot);
    }
}
