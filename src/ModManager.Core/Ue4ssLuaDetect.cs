namespace ModManager.Core;

/// <summary>The verdict for a candidate UE4SS Lua mod: whether it is one, and the top-level
/// folder name the archive uses for it (the directory that should land under ue4ss\Mods).</summary>
public sealed record Ue4ssLuaVerdict(bool IsLuaMod, string? ModFolderName);

/// <summary>
/// Recognizes a UE4SS Lua-mod ARCHIVE structure: a top-level folder containing either
/// <c>Scripts/*.lua</c> or <c>enabled.txt + dlls/*.dll</c>. A pak/ucas/utoc anywhere VETOES
/// (that's a content mod, not a Lua mod). Pure - no filesystem, no Electron.
/// </summary>
public static class Ue4ssLuaDetect
{
    private static readonly string[] PakExtensions = { ".pak", ".ucas", ".utoc" };

    public static Ue4ssLuaVerdict Detect(IEnumerable<string> zipEntryNames)
    {
        var names = (zipEntryNames ?? Enumerable.Empty<string>())
            .Select(n => (n ?? "").Replace('\\', '/').TrimStart('/'))
            .Where(n => n.Length > 0)
            .ToList();
        if (names.Count == 0) return new Ue4ssLuaVerdict(false, null);

        // Veto: any pak/content file means this is a content mod, not a Lua mod. Wins regardless of
        // nesting depth — a content mod that happens to ship a Scripts folder is still a content mod.
        foreach (var n in names)
            if (PakExtensions.Contains(System.IO.Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
                return new Ue4ssLuaVerdict(false, null);

        // Consider EVERY folder prefix as a candidate mod root, not just the top segment. Nexus
        // archives commonly wrap the mod folder in a version folder (<version>/<mod>/Scripts/main.lua),
        // and the mod that should land under ue4ss\Mods is the inner one. We collect each folder that
        // owns a UE4SS signature, then pick the DEEPEST (closest to the Scripts/dlls), so the version
        // wrapper never wins over the real mod folder. Among equal depths, first by archive order.
        var candidates = new List<string>();
        foreach (var n in names)
        {
            var luaUnder = TrimToParentOf(n, "/scripts/", ".lua");
            if (luaUnder is not null && !candidates.Contains(luaUnder, StringComparer.OrdinalIgnoreCase))
                candidates.Add(luaUnder);
        }
        // enabled.txt + a dll under dlls/ in the SAME folder is the native-mod signature.
        foreach (var n in names)
        {
            if (!n.EndsWith("/enabled.txt", StringComparison.OrdinalIgnoreCase)) continue;
            var folder = n[..^"/enabled.txt".Length];
            var hasDll = names.Any(p =>
                p.StartsWith(folder + "/dlls/", StringComparison.OrdinalIgnoreCase) &&
                p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (hasDll && !candidates.Contains(folder, StringComparer.OrdinalIgnoreCase))
                candidates.Add(folder);
        }
        if (candidates.Count == 0) return new Ue4ssLuaVerdict(false, null);

        // Deepest folder wins (most path segments); ties keep archive order (stable on first-seen).
        var best = candidates
            .Select((folder, idx) => (folder, depth: folder.Count(c => c == '/'), idx))
            .OrderByDescending(x => x.depth).ThenBy(x => x.idx)
            .First().folder;
        var modName = best.Contains('/') ? best[(best.LastIndexOf('/') + 1)..] : best;
        return new Ue4ssLuaVerdict(true, modName);
    }

    /// <summary>If <paramref name="path"/> is "&lt;folder&gt;<paramref name="midSegment"/>&lt;file&gt;<paramref name="ext"/>"
    /// (case-insensitive), returns "&lt;folder&gt;"; otherwise null. E.g. ("A/B/Scripts/main.lua",
    /// "/scripts/", ".lua") -> "A/B".</summary>
    private static string? TrimToParentOf(string path, string midSegment, string ext)
    {
        if (!path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return null;
        var idx = path.IndexOf(midSegment, StringComparison.OrdinalIgnoreCase);
        return idx <= 0 ? null : path[..idx];
    }
}
