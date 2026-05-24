namespace ModManager.Core;

/// <summary>A recognized direct-inject mod: a friendly name, a kind chip, and the file/folder we saw.</summary>
public sealed record DirectInjectMod(string Name, string Kind, string Evidence);

/// <summary>
/// Recognizes FromSoftware mods that don't use Mod Engine 2 — the loose-file kind dropped straight
/// into the game's exe folder (ReShade, Seamless Co-op, frame-gen loaders, a replaced
/// regulation.bin). Loose files there are otherwise indistinguishable from the game's own, so this
/// is a curated signature catalog, not a heuristic. Pure: the caller supplies the folder listing.
/// </summary>
public static class DirectInject
{
    // Files/Dirs match by exact name; FileContains matches anywhere in a filename (for mods whose
    // exact filename varies between releases, e.g. ultrawide fixes). Empty arrays just don't match.
    private sealed record Signature(string Name, string Kind, string[] Files, string[] Dirs, string[] FileContains);

    // Each mod's tell-tale files/dirs. First matching evidence wins for the label; one hit per mod.
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

    public static IReadOnlyList<DirectInjectMod> Detect(IEnumerable<string> files, IEnumerable<string> dirs)
    {
        var fileList = (files ?? Enumerable.Empty<string>()).Select(Norm).Where(n => n.Length > 0).ToList();
        var fileSet = new HashSet<string>(fileList, StringComparer.OrdinalIgnoreCase);
        var dirSet = new HashSet<string>((dirs ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.OrdinalIgnoreCase);

        var found = new List<DirectInjectMod>();
        foreach (var sig in Catalog)
        {
            var evidence = sig.Files.FirstOrDefault(fileSet.Contains)
                           ?? sig.Dirs.FirstOrDefault(dirSet.Contains)
                           ?? fileList.FirstOrDefault(f => sig.FileContains.Any(c => f.Contains(c, StringComparison.OrdinalIgnoreCase)));
            if (evidence is not null) found.Add(new DirectInjectMod(sig.Name, sig.Kind, evidence));
        }
        return found;
    }

    private static string Norm(string s) => (s ?? "").Trim();
}
