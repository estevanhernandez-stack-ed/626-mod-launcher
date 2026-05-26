using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// A recognized direct-inject mod: a friendly name, a kind chip, the file/folder we saw, and the
/// full set of mod-owned entries (files/folders) present — the things a toggle moves.
/// </summary>
public sealed record DirectInjectMod(string Name, string Kind, string Evidence, IReadOnlyList<string> Entries);

/// <summary>
/// Recognizes — and reversibly enables/disables — FromSoftware mods that don't use Mod Engine 2:
/// the loose-file kind dropped into the game's exe folder (ReShade, Seamless Co-op, frame-gen
/// loaders, a replaced regulation.bin, ultrawide fixes). Loose files there are indistinguishable
/// from the game's own, so this is a curated signature catalog, not a heuristic — which is what
/// makes moving a mod's owned set safe (the catalog never lists vanilla or shared-loader files).
/// Disabling MOVES the owned set to a holding folder; nothing is ever deleted.
/// </summary>
public static class DirectInject
{
    // The archive seam: one reader for all mod-archive reading (zip/7z/rar/tar via SharpCompress).
    // Static so the existing static install methods keep their shape. Replaces raw ZipFile.OpenRead.
    private static readonly IArchiveReader Archive = new SharpCompressArchiveReader();

    // True for any archive container we route through the seam (zip/7z/rar). Mirrors intake.
    private static bool IsArchive(string src)
        => Intake.ArchiveExtensions.Any(a => src.EndsWith(a, StringComparison.OrdinalIgnoreCase));

    // Files/Dirs match by exact name; FileContains matches anywhere in a filename (for mods whose
    // exact filename varies between releases, e.g. ultrawide fixes). Empty arrays just don't match.
    private sealed record Signature(string Name, string Kind, string[] Files, string[] Dirs, string[] FileContains);

    // Each mod's tell-tale files/dirs. Curated to mod-OWNED names only — never a shared proxy
    // loader (d3d12/dxgi/dinput8/version/winmm) or a vanilla game file — so moving the matched
    // set disables the mod without breaking the game or another mod.
    private static readonly Signature[] Catalog =
    {
        Sig("ReShade", "graphics", files: new[] { "reshadepreset.ini", "reshade.ini" }, dirs: new[] { "reshade-shaders" }),
        Sig("Seamless Co-op", "co-op", files: new[] { "ersc.dll", "ersc_settings.ini", "launch_elden_ring_seamlesscoop.exe" }, dirs: new[] { "seamlesscoop" }),
        Sig("ERSS2 Frame Gen", "upscaler", files: new[] { "erss-fg.dll", "erss-fg.toml", "erss2loader.log" }, dirs: new[] { "erss2" }),
        // Ultrawide/widescreen mods ship under varying filenames (ultrawidescreenfix.dll,
        // EldenRing_Ultrawide.dll, WidescreenFix.dll, ...) — match the name fragment, not an exact string.
        Sig("Ultrawide / Widescreen Fix", "display", contains: new[] { "ultrawide", "widescreen" }),
        Sig("Modded regulation.bin", "gameplay", files: new[] { "regulation.bin" }),
        Sig("DLL mod loader", "dll", files: new[] { "dinput8.dll" }),
    };

    private static Signature Sig(string name, string kind, string[]? files = null, string[]? dirs = null, string[]? contains = null)
        => new(name, kind, files ?? Array.Empty<string>(), dirs ?? Array.Empty<string>(), contains ?? Array.Empty<string>());

    /// <summary>
    /// Recognize which catalog-named direct-inject mods a zip archive INSTALLS, by running the same
    /// signature rules (<see cref="Signature"/>) the on-disk recognizer uses against the archive's
    /// entry list. Case-insensitive. Returns distinct mod names that matched; empty if nothing did.
    /// Pure - takes the entry names only, no IO.
    /// </summary>
    public static IReadOnlyList<string> MatchSignaturesInZip(IEnumerable<string> zipEntryNames)
    {
        var entries = (zipEntryNames ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n.Replace('\\', '/'))
            .ToList();
        if (entries.Count == 0) return Array.Empty<string>();

        // Pre-compute the things each Signature predicate looks at.
        var basenamesLower = entries
            .Select(n => System.IO.Path.GetFileName(n).ToLowerInvariant())
            .Where(n => n.Length > 0)
            .ToHashSet();
        var dirSegmentsLower = entries
            .SelectMany(n => n.Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1))
            .Select(s => s.ToLowerInvariant())
            .ToHashSet();

        var hits = new List<string>();
        foreach (var sig in Catalog)
        {
            if (sig.Files.Any(f => basenamesLower.Contains(f.ToLowerInvariant()))) { hits.Add(sig.Name); continue; }
            if (sig.Dirs.Any(d => dirSegmentsLower.Contains(d.ToLowerInvariant())))  { hits.Add(sig.Name); continue; }
            if (sig.FileContains.Any(f =>
                basenamesLower.Any(b => b.Contains(f, StringComparison.OrdinalIgnoreCase))))
            {
                hits.Add(sig.Name);
            }
        }
        return hits.Distinct().ToList();
    }

    /// <summary>The catalog label for the bare DLL mod loader — hidden once its individual mods are listed.</summary>
    public const string LoaderName = "DLL mod loader";
    private const string ModsDir = "mods";

    private const string MetaFile = "__626mod.json";

    // AtomicJson writes camelCase (Electron-shared convention); read tolerant of casing.
    private static readonly JsonSerializerOptions MetaJson = new() { PropertyNameCaseInsensitive = true };

    private sealed class DisabledMeta
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
        public List<string> Entries { get; set; } = new();
    }

    public static IReadOnlyList<DirectInjectMod> Detect(IEnumerable<string> files, IEnumerable<string> dirs)
    {
        var fileList = (files ?? Enumerable.Empty<string>()).Select(Norm).Where(n => n.Length > 0).ToList();
        var dirList = (dirs ?? Enumerable.Empty<string>()).Select(Norm).Where(n => n.Length > 0).ToList();
        var fileSet = new HashSet<string>(fileList, StringComparer.OrdinalIgnoreCase);
        var dirSet = new HashSet<string>(dirList, StringComparer.OrdinalIgnoreCase);

        var found = new List<DirectInjectMod>();
        foreach (var sig in Catalog)
        {
            // Every owned entry present in the folder — what a toggle will move, most-specific first.
            var entries = new List<string>();
            entries.AddRange(dirList.Where(d => sig.Dirs.Contains(d, StringComparer.OrdinalIgnoreCase)));
            entries.AddRange(fileList.Where(f => sig.Files.Contains(f, StringComparer.OrdinalIgnoreCase)));
            entries.AddRange(fileList.Where(f => sig.FileContains.Any(c => f.Contains(c, StringComparison.OrdinalIgnoreCase))));
            if (entries.Count == 0) continue;
            found.Add(new DirectInjectMod(sig.Name, sig.Kind, entries[0], entries));
        }
        return found;
    }

    /// <summary>
    /// The individual mods run by a DLL mod loader (techiew's): each DLL in its <c>mods\</c> folder
    /// is its own mod, owning that DLL plus a same-named config folder if present. Entries are
    /// relative to the play folder (<c>mods\Name.dll</c>) so the toggle moves them like any other.
    /// </summary>
    public static IReadOnlyList<DirectInjectMod> DetectLoaderMods(IEnumerable<string> modsFiles, IEnumerable<string> modsDirs)
    {
        var dirSet = new HashSet<string>((modsDirs ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.OrdinalIgnoreCase);
        var result = new List<DirectInjectMod>();
        foreach (var f in (modsFiles ?? Enumerable.Empty<string>()).Select(Norm))
        {
            if (!f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            var stem = Path.GetFileNameWithoutExtension(f);
            var entries = new List<string> { Path.Combine(ModsDir, f) };
            if (dirSet.Contains(stem)) entries.Add(Path.Combine(ModsDir, stem)); // its config folder
            result.Add(new DirectInjectMod(Prettify(stem), LoaderModKind(stem), entries[0], entries));
        }
        return result;
    }

    // "UltrawideFix" -> "Ultrawide Fix" (space before a capital that follows a lower/digit).
    private static string Prettify(string name) => Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

    private static string LoaderModKind(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("ultrawide") || n.Contains("widescreen") || n.Contains("fov") || n.Contains("resolution")) return "display";
        if (n.Contains("vignette") || n.Contains("chromatic") || n.Contains("bloom") || n.Contains("reshade")) return "graphics";
        return "tweak";
    }

    /// <summary>Disable: move the mod's owned entries into a per-mod holding folder, then record what moved.
    /// Rolls back any partial move on failure so the mod is never left half-disabled.</summary>
    public static void Disable(string playFolder, string holdingRoot, DirectInjectMod mod)
    {
        var dir = Path.Combine(holdingRoot, EnginePresets.Slugify(mod.Name));
        Directory.CreateDirectory(dir);

        var moved = new List<string>();
        try
        {
            foreach (var entry in mod.Entries)
            {
                var src = Path.Combine(playFolder, entry);
                if (!Exists(src)) continue; // already gone — skip, don't fail
                MoveAny(src, Path.Combine(dir, entry));
                moved.Add(entry);
            }
        }
        catch (Exception e)
        {
            foreach (var entry in moved)
            {
                try { MoveAny(Path.Combine(dir, entry), Path.Combine(playFolder, entry)); } catch { /* best effort */ }
            }
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
            throw new InvalidOperationException($"Couldn't disable \"{mod.Name}\" ({e.Message}) — is the game running?", e);
        }

        var meta = new DisabledMeta { Name = mod.Name, Kind = mod.Kind, Entries = moved };
        AtomicJson.WriteJsonAtomic(Path.Combine(dir, MetaFile), meta);
    }

    /// <summary>Enable: move a disabled mod's entries back into the play folder (skipping any whose
    /// name is already taken — a reinstalled live copy is never clobbered), then clear the holding folder.</summary>
    public static void Enable(string playFolder, string holdingRoot, string modName)
    {
        var dir = Path.Combine(holdingRoot, EnginePresets.Slugify(modName));
        var meta = ReadMeta(dir);
        if (meta is null) return;
        foreach (var entry in meta.Entries)
        {
            var src = Path.Combine(dir, entry);
            var dest = Path.Combine(playFolder, entry);
            if (!Exists(src) || Exists(dest)) continue;
            MoveAny(src, dest);
        }
        try { File.Delete(Path.Combine(dir, MetaFile)); } catch { /* best effort */ }
        try { Directory.Delete(dir, recursive: true); } catch { /* may hold un-restored entries */ }
    }

    // ---------- install (drop a zip / files into the exe folder) ----------

    /// <summary>
    /// Install dropped sources into the game's exe folder: zips are extracted (a single wrapping
    /// folder flattened), loose files/folders copied in. Path-traversal entries are refused and
    /// existing files are never overwritten — both surfaced as skips. Mirrors the intake contract.
    /// </summary>
    public static IntakeResult Install(string playFolder, IEnumerable<string> sourcePaths)
    {
        var result = new IntakeResult();
        Directory.CreateDirectory(playFolder);
        foreach (var src in sourcePaths ?? Enumerable.Empty<string>())
        {
            try
            {
                if (Directory.Exists(src)) InstallDir(src, playFolder, result);
                else if (IsArchive(src)) InstallZip(src, playFolder, result);
                else InstallFile(src, Path.GetFileName(src), playFolder, result);
            }
            catch (Exception e) { result.Skipped.Add(new SkippedItem(Path.GetFileName(src), e.Message)); }
        }
        return result;
    }

    /// <summary>Classify a drop against the play folder into add / collision / unsafe — no writes.</summary>
    public static IntakePlan Plan(string playFolder, IEnumerable<string> sourcePaths)
    {
        var add = new List<IntakeItem>();
        var collisions = new List<IntakeCollision>();
        var unsafeItems = new List<SkippedItem>();

        void Consider(string rel, string existingAbsDir, string incoming)
        {
            var dest = Path.Combine(existingAbsDir, rel);
            if (!IsUnder(playFolder, dest)) { unsafeItems.Add(new SkippedItem(rel, "unsafe path")); return; }
            var name = Path.GetFileName(rel);
            if (Exists(dest)) collisions.Add(new IntakeCollision(name, rel, dest, incoming));
            else add.Add(new IntakeItem(name, rel, incoming));
        }

        foreach (var src in sourcePaths ?? Enumerable.Empty<string>())
        {
            try
            {
                if (Directory.Exists(src))
                {
                    // Treat the dropped folder's own name as a single wrapper (like a zip's wrapping
                    // folder) and strip it, so its CONTENTS install relative to the target — never
                    // nested under "SomeMod v1.2\". Inner subfolders are preserved.
                    var baseName = new DirectoryInfo(src).Name;
                    foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                    {
                        var entryName = baseName + "/" + Path.GetRelativePath(src, file).Replace('\\', '/');
                        var rel = SafeRelative(entryName, baseName);
                        if (rel is null) { unsafeItems.Add(new SkippedItem(entryName, "unsafe path")); continue; }
                        Consider(rel, playFolder, file);
                    }
                }
                else if (IsArchive(src))
                {
                    using var zip = Archive.Open(src);
                    var names = zip.EntryNames; // file entries only (dirs excluded by the seam)
                    var prefix = WrapperPrefix(names);
                    foreach (var entryName in names)
                    {
                        var rel = SafeRelative(entryName, prefix);
                        if (rel is null) { unsafeItems.Add(new SkippedItem(entryName, "unsafe path")); continue; }
                        Consider(rel, playFolder, $"{src}!{entryName}");
                    }
                }
                else Consider(Path.GetFileName(src), playFolder, src);
            }
            catch (Exception e) { unsafeItems.Add(new SkippedItem(Path.GetFileName(src), e.Message)); }
        }
        return new IntakePlan(add, collisions, unsafeItems);
    }

    private static void CopyIncoming(string incoming, string destAbs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
        var bang = incoming.IndexOf('!');
        if (bang < 0) { File.Copy(incoming, destAbs, overwrite: true); return; }
        using var zip = Archive.Open(incoming[..bang]);
        zip.Extract(incoming[(bang + 1)..], destAbs, overwrite: true);
    }

    /// <summary>Execute a play-folder plan: install new files, back-up-then-replace chosen collisions, skip the rest.</summary>
    public static IntakeResult Execute(string playFolder, string replacedRoot, IntakePlan plan, ISet<string> replaceRelPaths)
    {
        var result = new IntakeResult();
        foreach (var u in plan.Unsafe) result.Skipped.Add(u);
        Directory.CreateDirectory(playFolder);
        string? batch = null;
        string Batch() => batch ??= ReplacedStore.NewBatch(replacedRoot);

        foreach (var item in plan.ToAdd)
        {
            try { CopyIncoming(item.IncomingSource, Path.Combine(playFolder, item.RelPath)); result.Added.Add(item.RelPath); }
            catch (Exception e) { result.Skipped.Add(new SkippedItem(item.Name, e.Message)); }
        }
        var manifest = new List<ReplacedStore.ReplacedEntry>();
        foreach (var col in plan.Collisions)
        {
            if (!replaceRelPaths.Contains(col.RelPath)) { result.Skipped.Add(new SkippedItem(col.Name, "kept existing")); continue; }
            string? backupPath = null;
            try
            {
                backupPath = ReplacedStore.Backup(col.ExistingPath, col.RelPath, Batch());
                CopyIncoming(col.IncomingSource, col.ExistingPath);
                manifest.Add(new ReplacedStore.ReplacedEntry(col.ExistingPath, col.RelPath, DateTime.UtcNow));
                result.Updated.Add(col.RelPath);
            }
            catch (Exception e)
            {
                // roll back the partial move so the original is never left missing
                try { if (backupPath != null && File.Exists(backupPath) && !File.Exists(col.ExistingPath)) File.Move(backupPath, col.ExistingPath); }
                catch { /* best effort */ }
                result.Skipped.Add(new SkippedItem(col.Name, e.Message));
            }
        }
        if (batch != null && manifest.Count > 0) ReplacedStore.WriteManifest(batch, manifest);
        return result;
    }

    /// <summary>The single top-level folder that wraps every zip entry (to flatten), or null when
    /// files sit at the root, entries span multiple top folders, or the prefix is a traversal.</summary>
    public static string? WrapperPrefix(IEnumerable<string> entryNames)
    {
        var names = (entryNames ?? Enumerable.Empty<string>())
            .Select(n => n.Replace('\\', '/').TrimStart('/')).Where(n => n.Length > 0).ToList();
        if (names.Count == 0) return null;
        string? top = null;
        foreach (var n in names)
        {
            var slash = n.IndexOf('/');
            if (slash < 0) return null; // a root-level file — no single wrapper
            var seg = n[..slash];
            if (seg is "." or "..") return null; // never flatten a traversal
            if (top is null) top = seg;
            else if (!string.Equals(top, seg, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return top;
    }

    /// <summary>A safe relative destination for a zip entry (wrapper stripped), or null for a
    /// directory entry or any path that tries to escape via traversal / absolute / drive root.</summary>
    public static string? SafeRelative(string entryName, string? stripPrefix)
    {
        var n = entryName.Replace('\\', '/').TrimStart('/');
        if (n.Length == 0 || n.EndsWith("/")) return null; // directory entry
        if (stripPrefix is not null)
        {
            var p = stripPrefix.TrimEnd('/') + "/";
            if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) n = n[p.Length..];
        }
        if (n.Length == 0) return null;
        var segs = n.Split('/');
        if (segs.Any(s => s is "" or "." or "..")) return null;     // traversal / empty segment
        if (n.Length > 1 && n[1] == ':') return null;               // drive-rooted
        return n.Replace('/', Path.DirectorySeparatorChar);
    }

    private static void InstallZip(string zipPath, string playFolder, IntakeResult result)
    {
        using var zip = Archive.Open(zipPath);
        var names = zip.EntryNames; // file entries only (dirs excluded by the seam)
        var prefix = WrapperPrefix(names);
        foreach (var entryName in names)
        {
            var rel = SafeRelative(entryName, prefix);
            if (rel is null) { result.Skipped.Add(new SkippedItem(entryName, "unsafe path")); continue; }
            var dest = Path.Combine(playFolder, rel);
            if (!IsUnder(playFolder, dest)) { result.Skipped.Add(new SkippedItem(rel, "unsafe path")); continue; }
            if (Exists(dest)) { result.Skipped.Add(new SkippedItem(rel, "already present")); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            zip.Extract(entryName, dest, overwrite: false);
            result.Added.Add(rel);
        }
    }

    private static void InstallFile(string src, string name, string playFolder, IntakeResult result)
    {
        var dest = Path.Combine(playFolder, name);
        if (Exists(dest)) { result.Skipped.Add(new SkippedItem(name, "already present")); return; }
        File.Copy(src, dest);
        result.Added.Add(name);
    }

    private static void InstallDir(string src, string playFolder, IntakeResult result)
    {
        // Strip the dropped folder's own name (the single wrapper, like a zip) so its contents
        // install relative to the play folder — not nested under "SomeMod\". Inner folders survive.
        var baseName = new DirectoryInfo(src).Name;
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var entryName = baseName + "/" + Path.GetRelativePath(src, file).Replace('\\', '/');
            var rel = SafeRelative(entryName, baseName);
            if (rel is null) { result.Skipped.Add(new SkippedItem(entryName, "unsafe path")); continue; }
            var dest = Path.Combine(playFolder, rel);
            if (!IsUnder(playFolder, dest)) { result.Skipped.Add(new SkippedItem(rel, "unsafe path")); continue; }
            if (Exists(dest)) { result.Skipped.Add(new SkippedItem(rel, "already present")); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
            result.Added.Add(rel);
        }
    }

    private static bool IsUnder(string root, string path)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The currently-disabled direct-inject mods, read from holding-folder metadata.</summary>
    public static IReadOnlyList<DirectInjectMod> ListDisabled(string holdingRoot)
    {
        var result = new List<DirectInjectMod>();
        if (!Directory.Exists(holdingRoot)) return result;
        foreach (var dir in Directory.GetDirectories(holdingRoot))
        {
            var meta = ReadMeta(dir);
            if (meta is null) continue;
            result.Add(new DirectInjectMod(meta.Name, meta.Kind, meta.Entries.FirstOrDefault() ?? meta.Name, meta.Entries));
        }
        return result;
    }

    private static DisabledMeta? ReadMeta(string dir)
    {
        try { return JsonSerializer.Deserialize<DisabledMeta>(File.ReadAllText(Path.Combine(dir, MetaFile)), MetaJson); }
        catch { return null; }
    }

    private static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);

    private static void MoveAny(string src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        try
        {
            if (Directory.Exists(src)) Directory.Move(src, dest);
            else File.Move(src, dest);
        }
        catch (IOException) // cross-volume (game on a different drive than the data dir): copy then delete
        {
            if (Directory.Exists(src)) { CopyDir(src, dest); Directory.Delete(src, recursive: true); }
            else { File.Copy(src, dest, overwrite: false); File.Delete(src); }
        }
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    private static string Norm(string s) => (s ?? "").Trim();
}
