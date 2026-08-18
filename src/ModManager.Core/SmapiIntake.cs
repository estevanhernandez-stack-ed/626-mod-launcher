namespace ModManager.Core;

/// <summary>
/// Where the files of a dropped SMAPI archive belong. Pure — decided from entry names alone, no disk.
///
/// <para><b>A SMAPI mod is a FOLDER holding a <c>manifest.json</c></b>, never a loose file. Intake did
/// not know that: <c>smapi</c> has no <c>loose-root</c> form and is not direct-inject-backed, so a
/// dropped archive reached <see cref="Scanner.PlanIntake"/> with the empty→<c>["pak"]</c> substitution
/// applied, and any <c>.pak</c> inside it classified as a mod and landed flat in <c>Mods/</c> (A9).</para>
///
/// <para>Taking that substitution away from intake is not the fix — A7 settled that. An extension-less
/// registration IS a pak game to <c>FileRe</c> and <c>ModKey</c>, so intake would start refusing mods
/// the scanner's listing lane happily shows, and the two lanes have to keep agreeing. The fix is to
/// recognise the shape instead: find the marker, place the folder that holds it.</para>
/// </summary>
public static class SmapiIntake
{
    private const string Marker = "manifest.json";

    /// <summary>True when this archive looks like it carries SMAPI mods — it declares a
    /// <c>manifest.json</c> somewhere. Deliberately paired with an engine check at the call site
    /// rather than used alone: a pak mod that happens to ship a manifest.json is not a SMAPI mod, and
    /// the marker on its own would claim it.</summary>
    public static bool LooksLikeSmapi(IEnumerable<string>? entryNames)
        => entryNames is not null && entryNames.Any(IsMarker);

    /// <summary>
    /// Map each archive entry to where it should land, relative to the mod location. Entries that are
    /// not inside a mod folder are absent from the result — that is what stops a stray <c>.pak</c>
    /// sitting beside the folder from being placed flat.
    /// </summary>
    /// <param name="entryNames">Every file entry in the archive.</param>
    /// <param name="fallbackName">Mod folder name to use when the marker sits at the archive ROOT, in
    /// which case the archive itself is the mod folder and its name is the only name available. Pass
    /// the archive's filename without extension.</param>
    public static IReadOnlyDictionary<string, string> Plan(
        IEnumerable<string>? entryNames, string? fallbackName = null)
    {
        var entries = (entryNames ?? Array.Empty<string>()).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (entries.Count == 0) return result;

        // Every folder that directly contains a manifest.json. The IMMEDIATE parent, so a
        // version-wrapped archive (MyMod-1.2.3/MyMod/manifest.json) yields MyMod and not the wrapper —
        // the wrapper is packaging, the folder beside the marker is the mod.
        var roots = new List<(string Prefix, string ModName)>();
        foreach (var e in entries.Where(IsMarker))
        {
            var norm = Norm(e);
            var slash = norm.LastIndexOf('/');
            if (slash < 0)
            {
                // Marker at the archive root: the archive IS the mod folder.
                var name = Clean(fallbackName);
                if (name.Length == 0) continue;   // nothing to name it after — refuse rather than guess
                roots.Add(("", name));
                continue;
            }
            var folder = norm[..slash];
            var modName = folder[(folder.LastIndexOf('/') + 1)..];
            if (modName.Length > 0) roots.Add((folder + "/", modName));
        }
        if (roots.Count == 0) return result;

        // Longest prefix wins, so a mod nested inside another mod's folder is attributed to the
        // nearest marker rather than the outermost one.
        roots.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));

        foreach (var e in entries)
        {
            var norm = Norm(e);
            foreach (var (prefix, modName) in roots)
            {
                if (prefix.Length > 0 && !norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var below = prefix.Length > 0 ? norm[prefix.Length..] : norm;
                if (below.Length == 0) continue;
                result[e] = modName + "/" + below;
                break;
            }
        }
        return result;
    }

    private static bool IsMarker(string entry)
        => Path.GetFileName(Norm(entry)).Equals(Marker, StringComparison.OrdinalIgnoreCase);

    private static string Norm(string s) => s.Replace('\\', '/').TrimStart('/');

    private static string Clean(string? s) => (s ?? "").Trim().Trim('/', '\\');
}
