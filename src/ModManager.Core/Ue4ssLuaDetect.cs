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

        // Veto: any pak/content file means this is a content mod, not a Lua mod.
        foreach (var n in names)
            if (PakExtensions.Contains(System.IO.Path.GetExtension(n), StringComparer.OrdinalIgnoreCase))
                return new Ue4ssLuaVerdict(false, null);

        // Group entries by their TOP-LEVEL folder (the first path segment). The first group with
        // a matching signature wins - mirrors how save-mod detection picks the first valid GUID.
        var byTop = names
            .Select(n => new { Segs = n.Split('/'), Full = n })
            .Where(x => x.Segs.Length >= 2)
            .GroupBy(x => x.Segs[0], StringComparer.OrdinalIgnoreCase);

        foreach (var g in byTop)
        {
            var inFolder = g.Select(x => x.Full).ToList();
            var hasScriptsLua = inFolder.Any(p =>
                p.StartsWith(g.Key + "/Scripts/", StringComparison.OrdinalIgnoreCase) &&
                p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
            var hasEnabledTxt = inFolder.Any(p =>
                string.Equals(p, g.Key + "/enabled.txt", StringComparison.OrdinalIgnoreCase));
            var hasDllInDlls = inFolder.Any(p =>
                p.StartsWith(g.Key + "/dlls/", StringComparison.OrdinalIgnoreCase) &&
                p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (hasScriptsLua || (hasEnabledTxt && hasDllInDlls))
                return new Ue4ssLuaVerdict(true, g.Key);
        }
        return new Ue4ssLuaVerdict(false, null);
    }
}
