using SharpCompress.Archives;

namespace ModManager.Core.Tools;

public enum ToolClassification { Tool, Mod }

/// <summary>
/// Pure classifier — given a dropped archive path + the active game's engine + steamAppId,
/// returns whether the archive should be installed as a tool or routed through the existing
/// mod intake.
///
/// v1 is deterministic: when both heuristic-tool AND mod signatures are present, mod-intake
/// wins (the safer default — the mod path is mature, tool intake is new). The ambiguous-dialog
/// branch lives in the spec at v2.
///
/// Classification rules in priority order:
/// 1. Catalog match by zip filename + active engine + steamAppId → Tool(knownTool)
/// 2. Mod signature in archive (.pak, .lua under Scripts/, manifest.json) → Mod
/// 3. Heuristic-tool: any .exe / .bat / .ps1 / .cmd at any depth → Tool(null)
/// 4. Default Mod (lower-risk fallback)
/// </summary>
public static class ToolDetector
{
    private static readonly string[] ExecutableExtensions = { ".exe", ".bat", ".ps1", ".cmd" };

    public static (ToolClassification, KnownTool?) Classify(string archivePath, string engine, string steamAppId)
    {
        // 1. Catalog match by zip filename + applicable engine + steamAppId.
        var filename = Path.GetFileName(archivePath).ToLowerInvariant();
        foreach (var known in ToolCatalog.Catalog)
        {
            if (known.Engine != engine || known.SteamAppId != steamAppId) continue;
            foreach (var hint in known.ZipFilenameHints)
            {
                if (filename.Contains(hint.ToLowerInvariant()))
                    return (ToolClassification.Tool, known);
            }
        }

        // 2 + 3. Inspect archive contents — mod signature wins; else heuristic-tool; else mod.
        List<string> paths;
        try
        {
            using var arc = ArchiveFactory.OpenArchive(archivePath);
            paths = arc.Entries
                .Where(e => !e.IsDirectory)
                .Select(e => e.Key?.Replace('\\', '/') ?? "")
                .ToList();
        }
        catch
        {
            // Malformed archive — caller's existing intake will surface the real error.
            return (ToolClassification.Mod, null);
        }

        if (HasModSignature(paths)) return (ToolClassification.Mod, null);
        if (HasExecutable(paths)) return (ToolClassification.Tool, null);
        return (ToolClassification.Mod, null);
    }

    private static bool HasModSignature(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
        {
            var lower = p.ToLowerInvariant();
            if (lower.EndsWith(".pak")) return true;
            if (lower.Contains("/scripts/") && lower.EndsWith(".lua")) return true;
            if (lower.EndsWith("/manifest.json") || lower == "manifest.json") return true;
        }
        return false;
    }

    private static bool HasExecutable(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
        {
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (Array.IndexOf(ExecutableExtensions, ext) >= 0) return true;
        }
        return false;
    }
}
