using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// What one intake actually wrote: the archive it came from and every file it placed, relative to the
/// mod location.
///
/// <para>Keyed per SOURCE ARCHIVE rather than per row, because one archive routinely produces several
/// rows — Shop Tweaks ships four paks and each is a mod in the list — while the thing the launcher
/// needs to answer is "which install placed this file". Per-archive gives that directly; per-row would
/// have to reassemble it.</para>
/// </summary>
public sealed record ModInstallManifest(
    string InstallId,
    string SourceArchive,
    string Location,
    IReadOnlyList<string> Files,
    DateTime InstalledUtc)
{
    /// <summary>Display label for the install — the archive's name without its extension.</summary>
    public string DisplayName => Path.GetFileNameWithoutExtension(SourceArchive);
}

/// <summary>
/// Per-game record of what 626 placed, at <c>&lt;dataDir&gt;/installs/&lt;installId&gt;.json</c>.
///
/// <para>The provenance half of the mod-provenance design
/// (<c>docs/superpowers/specs/2026-08-18-mod-provenance-design.md</c>). Intake already computed the
/// file list as <see cref="IntakeResult.Added"/> and threw it away, so a row could never say which
/// files were its own and a toggle could only guess. With a record, a row IS a mod: it moves exactly
/// its files, and "does another install claim this file" becomes answerable rather than lucky.</para>
///
/// <para><b>A manifest means "we wrote these bytes."</b> Adoption never mints one — that decision is
/// in the spec and it is a reversibility law, not a preference: a manifest is what an uninstall reads
/// to decide what to delete, so minting one for files another manager placed would let 626 delete
/// what it never wrote, with no backup to restore from because there was never an install to
/// snapshot.</para>
///
/// <para>Mirrors <see cref="Frameworks.FrameworkRegistry"/> deliberately, down to the camelCase +
/// tolerant-read options: one more on-disk shape spelled the same way as the others.</para>
/// </summary>
public static class ModInstallRegistry
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static string RootFor(string gameDataDir) => Path.Combine(gameDataDir, "installs");

    /// <summary>Every install recorded for a game. Missing directory or a corrupt file yields fewer
    /// entries rather than an exception — one unreadable record must not hide the rest.</summary>
    public static IReadOnlyList<ModInstallManifest> List(string gameDataDir)
    {
        var root = RootFor(gameDataDir);
        if (!Directory.Exists(root)) return Array.Empty<ModInstallManifest>();

        var found = new List<ModInstallManifest>();
        foreach (var file in SafeEnumerate(root))
        {
            try
            {
                var m = JsonSerializer.Deserialize<ModInstallManifest>(File.ReadAllText(file), Json);
                if (m is not null && m.Files.Count > 0) found.Add(m);
            }
            catch { /* a torn record is skipped, never fatal */ }
        }
        return found;
    }

    /// <summary>Record an install. Atomic + camelCase per the on-disk rule.</summary>
    public static void Save(string gameDataDir, ModInstallManifest manifest)
    {
        var root = RootFor(gameDataDir);
        Directory.CreateDirectory(root);
        AtomicJson.WriteJsonAtomic(Path.Combine(root, manifest.InstallId + ".json"), manifest);
    }

    /// <summary>Forget an install. Removing the record does NOT remove files — callers that delete
    /// must do so first, so a crash between the two leaves files with a record rather than orphans
    /// with none.</summary>
    public static void Remove(string gameDataDir, string installId)
    {
        try
        {
            var path = Path.Combine(RootFor(gameDataDir), installId + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Which installs claim a given file (relative to the mod location). More than one is a
    /// real situation, not an error: two mods can ship the same shared library path, and that is
    /// precisely the case a disable must not resolve by deleting.</summary>
    public static IReadOnlyList<ModInstallManifest> ClaimsOn(string gameDataDir, string relPath)
        => List(gameDataDir).Where(m => m.Files.Any(f => SamePath(f, relPath))).ToList();

    /// <summary>A stable, filesystem-safe id for an archive. Same archive installed twice overwrites
    /// its own record rather than accumulating one per attempt.</summary>
    public static string IdFor(string sourceArchive)
    {
        var stem = Path.GetFileNameWithoutExtension(sourceArchive) ?? "install";
        var safe = new string(stem.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrEmpty(safe) ? "install" : safe;
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string p) => (p ?? "").Replace('\\', '/').Trim('/');

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        try { return Directory.EnumerateFiles(root, "*.json"); }
        catch { return Array.Empty<string>(); }
    }
}
