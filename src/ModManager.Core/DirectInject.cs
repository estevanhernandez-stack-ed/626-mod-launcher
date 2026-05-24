using System.Text.Json;

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
