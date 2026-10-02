namespace ModManager.Core;

/// <summary>A version-locked loader whose file is older than the game's executable.</summary>
/// <param name="Name">The loader's product name, e.g. <c>REFramework</c>.</param>
/// <param name="LoaderPath">The file the date was read from.</param>
/// <param name="GameExePath">The executable taken as the game's build (see <see cref="StaleLoaders.GameExecutable"/>).</param>
/// <param name="Url">Where a newer release is published, when known.</param>
public sealed record StaleLoader(
    string Name, string LoaderPath, DateTime LoaderUtc, string GameExePath, DateTime GameExeUtc, string? Url)
{
    /// <summary>One sentence a person or an agent can act on. It states both dates rather than a verdict,
    /// because a fresh install of the game after the loader's download looks exactly like a patch.</summary>
    public string Sentence =>
        $"{Name} ({Path.GetFileName(LoaderPath)}, {LoaderUtc:yyyy-MM-dd}) is older than the game's executable "
        + $"({Path.GetFileName(GameExePath)}, {GameExeUtc:yyyy-MM-dd}). {Name} usually needs a new release after "
        + "a game patch; if the game has been patched since you installed it, update it before launching"
        + (Url is null ? "." : $" ({Url}).");
}

/// <summary>
/// A17, the half the backlog called valuable: "your loader predates this game build".
///
/// <para>Seen live on Monster Hunter Wilds: a REFramework <c>dinput8.dll</c> dated seventeen months before
/// the game binary, and a game that crashed on start. Nothing in the launcher compared the two, though the
/// mismatch is a file date away.</para>
///
/// <para><b>Version-locked loaders only</b> (Este, 2026-10-02). These hook the game's own code, so a patch
/// routinely breaks them: REFramework, UE4SS, Elden Mod Loader, Mod Engine 2. Loaders that do not care
/// which build they load into (an ASI loader, ReShade, BepInEx's doorstop) stay out of the catalog: flagging
/// them after every Steam patch would teach people to ignore the warning.</para>
///
/// <para><b>Dates, not versions.</b> None of these publish a "supports build N" fact the launcher could
/// read. The loader's file date is its release date (archives keep it on extraction); the executable's is
/// when Steam last wrote it. So the check is honest about what it knows: the loader is older than the game
/// binary. A fresh install of the game after downloading the loader reads the same way, which is why the
/// sentence names both dates and the App lets the user mark the pair as checked.</para>
/// </summary>
public static class StaleLoaders
{
    /// <summary>The loader must be older than the executable by more than this. A loader and a game set up
    /// the same day are not a mismatch worth a chip.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromDays(1);

    /// <summary>A loader this check knows: any one of its files (relative to a probe root), and, when the
    /// file name is shared with other loaders, a sibling only this one ships.</summary>
    private sealed record VersionLocked(string Name, string[] AnyOf, string? Sibling, string? Url);

    private static readonly IReadOnlyList<VersionLocked> Catalog = new[]
    {
        // dinput8.dll alone is half a dozen products; the sibling is what makes it REFramework (the same
        // pairing ProxyLoaderRows names it by).
        new VersionLocked("REFramework", new[] { "dinput8.dll" }, "reframework",
            "https://github.com/praydog/REFramework/releases"),
        new VersionLocked("Elden Mod Loader", new[] { "dinput8.dll" }, "mod_loader_config.ini",
            "https://www.nexusmods.com/eldenring/mods/117"),
        // Relative to the UE project folder, which is one of the probe roots for a UE game. 3.x keeps the
        // runtime under ue4ss/; 2.x put it beside the executable.
        new VersionLocked("UE4SS", new[] { "Binaries/Win64/ue4ss/UE4SS.dll", "Binaries/Win64/UE4SS.dll" }, null,
            "https://github.com/UE4SS-RE/RE-UE4SS/releases"),
        // The DLL is what hooks the game; the launcher exe is the fallback when it is laid out differently.
        new VersionLocked("Mod Engine 2", new[] { "modengine2/bin/modengine2.dll", "modengine2_launcher.exe" }, null,
            "https://github.com/soulsmods/ModEngine2/releases"),
    };

    /// <summary>
    /// The executable taken as the game's build: the LARGEST <c>.exe</c> directly in a probe root or in its
    /// <c>Binaries/Win64</c> (where a UE game's <c>-Shipping.exe</c> lives). Null when there is none.
    ///
    /// <para>Largest, because the registration has no reliable "game exe" field: most Steam games only store
    /// a <c>steam://</c> target, and an exe target is often a mod launcher. Beside the real binary sit a
    /// UE bootstrap stub, crash reporters and mod launchers, all a fraction of its size.</para>
    /// </summary>
    public static (string Path, DateTime Utc)? GameExecutable(GameContext ctx)
    {
        FileInfo? best = null;
        foreach (var root in FrameworkDeps.ResolveProbeRoots(ctx))
            foreach (var dir in new[] { root, Path.Combine(root, "Binaries", "Win64") })
            {
                IEnumerable<FileInfo> exes;
                try { exes = Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*.exe").ToList() : Enumerable.Empty<FileInfo>(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                foreach (var f in exes)
                    if (best is null || f.Length > best.Length) best = f;
            }
        return best is null ? null : (best.FullName, best.LastWriteTimeUtc);
    }

    /// <summary>Every live version-locked loader older than the game's executable. A loader the launcher
    /// stepped aside for a vanilla launch is not at its path, so it is not reported.</summary>
    public static IReadOnlyList<StaleLoader> Find(GameContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.GameRoot) || !Directory.Exists(ctx.GameRoot)) return Array.Empty<StaleLoader>();
        if (GameExecutable(ctx) is not { } exe) return Array.Empty<StaleLoader>();

        var roots = FrameworkDeps.ResolveProbeRoots(ctx);
        var found = new List<StaleLoader>();
        foreach (var loader in Catalog)
        {
            if (FileFor(loader, roots) is not { } file) continue;
            DateTime loaderUtc;
            try { loaderUtc = File.GetLastWriteTimeUtc(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (exe.Utc - loaderUtc > Slack)
                found.Add(new StaleLoader(loader.Name, file, loaderUtc, exe.Path, exe.Utc, loader.Url));
        }
        return found;
    }

    /// <summary>The chip's sentence, or null when there is nothing to say: no stale loader, or the user
    /// already marked loaders as checked against this very executable (<see cref="GameEntry.LoaderCheckedExeUtc"/>).
    /// A patch rewrites the executable, so the next one brings the chip back.</summary>
    public static string? Summary(IReadOnlyList<StaleLoader> stale, DateTime? checkedExeUtc)
    {
        if (stale.Count == 0) return null;
        if (checkedExeUtc is { } seen && stale.All(s => s.GameExeUtc <= seen)) return null;
        return string.Join(" ", stale.Select(s => s.Sentence));
    }

    private static string? FileFor(VersionLocked loader, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (loader.Sibling is { } sibling && !Exists(Path.Combine(root, sibling))) continue;
            foreach (var rel in loader.AnyOf)
            {
                var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static bool Exists(string p)
    {
        try { return File.Exists(p) || Directory.Exists(p); }
        catch { return false; }
    }
}
