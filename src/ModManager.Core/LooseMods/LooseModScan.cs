namespace ModManager.Core.LooseMods;

/// <summary>By-nature detection of loose-file root mods: files that are RELIABLY mods regardless of
/// game (a game never ships .asi/.addon64; the proxy names are the ASI-loader convention). Emits the
/// same <see cref="DirectInjectMod"/> shape the catalog detector emits so listing/toggle/intake reuse
/// the proven DirectInject plumbing. Safety lines: standalone INIs and generic DLLs are NEVER claimed;
/// anything unmatched is invisible. Top-level only. Pure — the caller supplies the listing.
/// Phase-2 seam: this is one signal; a vanilla-diff signal composes alongside it later.</summary>
public static class LooseModScan
{
    private static readonly string[] ProxyNames =
        { "dinput8.dll", "version.dll", "winmm.dll", "d3d11.dll", "dxgi.dll", "winhttp.dll" };

    public static IReadOnlyList<DirectInjectMod> Detect(
        IReadOnlyList<string> topFiles, IReadOnlyList<string> topDirs, ISet<string>? alreadyOwned = null)
    {
        var owned = alreadyOwned ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Free(string name) => !owned.Contains(name);
        var mods = new List<DirectInjectMod>();

        // ASI plugins → "plugin"; ReShade addons → "shaders". Same-stem .ini/.txt/.log + same-stem dir group in.
        foreach (var (ext, kind) in new[] { (".asi", "plugin"), (".addon64", "shaders"), (".addon32", "shaders") })
        {
            foreach (var f in topFiles)
            {
                if (!f.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || !Free(f)) continue;
                var stem = Path.GetFileNameWithoutExtension(f);
                var entries = new List<string> { f };
                foreach (var cfgExt in new[] { ".ini", ".txt", ".log" })
                {
                    var cfg = topFiles.FirstOrDefault(x =>
                        x.Equals(stem + cfgExt, StringComparison.OrdinalIgnoreCase) && Free(x));
                    if (cfg is not null) entries.Add(cfg);
                }
                var dir = topDirs.FirstOrDefault(d => d.Equals(stem, StringComparison.OrdinalIgnoreCase) && Free(d));
                if (dir is not null) entries.Add(dir);
                mods.Add(new DirectInjectMod(stem, kind, $"loose {ext} in game root", entries));
            }
        }

        // Exact-name proxy loaders → "loader" (flagged by Kind; disable warns App-side).
        foreach (var p in ProxyNames)
        {
            var hit = topFiles.FirstOrDefault(f => f.Equals(p, StringComparison.OrdinalIgnoreCase) && Free(f));
            if (hit is not null)
                mods.Add(new DirectInjectMod(Path.GetFileNameWithoutExtension(hit) + " (ASI loader)",
                    "loader", "proxy loader DLL in game root", new List<string> { hit }));
        }

        return mods;
    }
}
