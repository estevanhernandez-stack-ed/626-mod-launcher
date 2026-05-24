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
    private sealed record Signature(string Name, string Kind, string[] Files, string[] Dirs);

    // Each mod's tell-tale files/dirs. First matching evidence wins for the label; one hit per mod.
    private static readonly Signature[] Catalog =
    {
        new("ReShade", "graphics",
            new[] { "reshadepreset.ini", "reshade.ini" }, new[] { "reshade-shaders" }),
        new("Seamless Co-op", "co-op",
            new[] { "ersc.dll", "ersc_settings.ini", "launch_elden_ring_seamlesscoop.exe" }, new[] { "seamlesscoop" }),
        new("ERSS2 Frame Gen", "upscaler",
            new[] { "erss-fg.dll", "erss-fg.toml", "erss2loader.log" }, new[] { "erss2" }),
        new("Modded regulation.bin", "gameplay",
            new[] { "regulation.bin" }, Array.Empty<string>()),
        new("DLL mod loader", "dll",
            new[] { "dinput8.dll" }, Array.Empty<string>()),
    };

    public static IReadOnlyList<DirectInjectMod> Detect(IEnumerable<string> files, IEnumerable<string> dirs)
    {
        var fileSet = new HashSet<string>((files ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.OrdinalIgnoreCase);
        var dirSet = new HashSet<string>((dirs ?? Enumerable.Empty<string>()).Select(Norm), StringComparer.OrdinalIgnoreCase);

        var found = new List<DirectInjectMod>();
        foreach (var sig in Catalog)
        {
            var evidence = sig.Files.FirstOrDefault(fileSet.Contains)
                           ?? sig.Dirs.FirstOrDefault(dirSet.Contains);
            if (evidence is not null) found.Add(new DirectInjectMod(sig.Name, sig.Kind, evidence));
        }
        return found;
    }

    private static string Norm(string s) => (s ?? "").Trim();
}
