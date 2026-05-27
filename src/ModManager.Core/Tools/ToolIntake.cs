using SharpCompress.Archives;
using SharpCompress.Common;

namespace ModManager.Core.Tools;

/// <summary>
/// Result of <see cref="ToolIntake.Install"/>. When the runnable can't be picked
/// deterministically, <see cref="Entry"/>'s <c>Runnable</c> is empty and <see cref="Candidates"/>
/// holds the legit choices the caller should hand to the install dialog for user pick.
/// </summary>
public sealed record ToolInstallResult(ToolEntry Entry, IReadOnlyList<string> Candidates);

/// <summary>
/// Pure intake for catalog-known + heuristic third-party tools. Extracts a zip into
/// <c>&lt;gameDataDir&gt;/tools/&lt;tool-id&gt;/</c>, picks the runnable (catalog hint > heuristic),
/// writes the tools.json registry entry, and returns the resulting <see cref="ToolEntry"/>
/// alongside any unresolved candidates.
///
/// Runnable picker priority:
/// 1. Catalog entry's ExpectedRunnableHints — first hint that exists wins.
/// 2. Filter out installer/setup/update/dep patterns from the runnable set first.
/// 3. Single remaining .exe / .bat / .ps1 / .cmd → use it.
/// 4. Multiple remain, one filename stem matches the zip name or DisplayName → use it.
/// 5. Still ambiguous → return empty Runnable + the filtered candidates list.
/// </summary>
public static class ToolIntake
{
    private static readonly string[] ExecutableExtensions = { ".exe", ".bat", ".ps1", ".cmd" };
    private static readonly string[] InstallerFilterSubstrings = { "install", "setup", "update", "dep" };

    public static ToolInstallResult Install(string archivePath, string gameDataDir, KnownTool? knownTool)
    {
        var toolId = knownTool?.ToolId ?? SlugFromArchiveName(archivePath);
        var displayName = knownTool?.DisplayName ?? PrettyFromSlug(toolId);
        var installDir = Path.Combine(gameDataDir, "tools", toolId);

        Directory.CreateDirectory(installDir);
        ExtractAll(archivePath, installDir);

        var runnables = FindRunnables(installDir);
        var (pickedRunnable, candidates) = PickRunnable(runnables, knownTool, archivePath);

        var entry = new ToolEntry(
            ToolId: toolId,
            DisplayName: displayName,
            InstallDir: installDir,
            Runnable: pickedRunnable,
            EditsSaves: knownTool?.EditsSaves ?? false,
            GetUrl: knownTool?.GetUrl,
            Source: knownTool is null ? "user" : "catalog");

        // Replace any prior entry with the same ToolId so re-install is idempotent.
        var existing = ToolRegistry.Load(gameDataDir).Tools;
        var next = existing.Where(t => t.ToolId != toolId).Append(entry).ToList();
        ToolRegistry.Save(gameDataDir, next);

        return new ToolInstallResult(entry, candidates);
    }

    private static void ExtractAll(string archivePath, string targetDir)
    {
        using var arc = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in arc.Entries.Where(e => !e.IsDirectory))
        {
            entry.WriteToDirectory(targetDir, new ExtractionOptions
            {
                ExtractFullPath = true,
                Overwrite = true,
            });
        }
    }

    private static List<string> FindRunnables(string installDir)
    {
        return Directory.EnumerateFiles(installDir, "*.*", SearchOption.AllDirectories)
            .Where(f => Array.IndexOf(ExecutableExtensions, Path.GetExtension(f).ToLowerInvariant()) >= 0)
            .Select(f => Path.GetRelativePath(installDir, f).Replace('\\', '/'))
            .ToList();
    }

    private static (string runnable, IReadOnlyList<string> candidates) PickRunnable(
        IReadOnlyList<string> runnables,
        KnownTool? known,
        string archivePath)
    {
        // 1. Catalog hint match (case-insensitive on filename).
        if (known is not null)
        {
            foreach (var hint in known.ExpectedRunnableHints)
            {
                var hit = runnables.FirstOrDefault(r =>
                    string.Equals(Path.GetFileName(r), hint, StringComparison.OrdinalIgnoreCase));
                if (hit is not null) return (hit, Array.Empty<string>());
            }
        }

        // 2. Filter installer/setup/update/dep patterns out of the candidate set.
        var filtered = runnables
            .Where(r => !InstallerFilterSubstrings.Any(s =>
                Path.GetFileName(r).Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // 3. Single legit runnable → use it.
        if (filtered.Count == 1) return (filtered[0], Array.Empty<string>());

        // 4. Multiple — one stem matches the zip name or the catalog DisplayName?
        var zipNameStem = Path.GetFileNameWithoutExtension(archivePath).ToLowerInvariant();
        var displayNameStem = known?.DisplayName?.Replace(' ', '_').ToLowerInvariant() ?? "";

        var nameMatch = filtered.FirstOrDefault(r =>
        {
            var stem = Path.GetFileNameWithoutExtension(r).ToLowerInvariant();
            return stem == zipNameStem
                || (!string.IsNullOrEmpty(displayNameStem) && stem == displayNameStem);
        });
        if (nameMatch is not null) return (nameMatch, Array.Empty<string>());

        // 5. Still ambiguous — caller surfaces the dialog with these candidates.
        return ("", filtered);
    }

    private static string SlugFromArchiveName(string archivePath)
    {
        var stem = Path.GetFileNameWithoutExtension(archivePath).ToLowerInvariant();
        var chars = stem.Select(c => char.IsLetterOrDigit(c) ? c : '-');
        var slug = new string(chars.ToArray());
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static string PrettyFromSlug(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
