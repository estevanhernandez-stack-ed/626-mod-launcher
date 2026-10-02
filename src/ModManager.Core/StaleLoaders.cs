using System.Globalization;

namespace ModManager.Core;

/// <summary>A version-locked loader whose file is older than the game's executable.</summary>
/// <param name="Name">The loader's product name, e.g. <c>REFramework</c>.</param>
/// <param name="LoaderPath">The file the date was read from.</param>
/// <param name="Url">Where a newer release is published, when known.</param>
public sealed record StaleLoader(string Name, string LoaderPath, DateTime LoaderUtc, string? Url);

/// <summary>
/// The result of one check: the executable taken as the game's build, and every live version-locked loader
/// older than it. The executable is stated once because every loader was compared against the same one,
/// and it is what "Mark as checked" records.
/// </summary>
public sealed record StaleLoaderReport(string? GameExePath, DateTime GameExeUtc, IReadOnlyList<StaleLoader> Loaders)
{
    public static readonly StaleLoaderReport None = new(null, default, Array.Empty<StaleLoader>());

    /// <summary>Whether the user already marked loaders as checked against this very executable
    /// (<see cref="GameEntry.LoaderCheckedExeUtc"/>). A patch rewrites the executable, so the next one
    /// undoes it.</summary>
    public bool CheckedAgainst(DateTime? checkedExeUtc) => checkedExeUtc is { } seen && GameExeUtc <= seen;

    /// <summary>One sentence per loader, a person or an agent can act on. It states both dates rather than
    /// a verdict, because a fresh install of the game after the loader's download looks exactly like a
    /// patch. Local dates, because people compare them with what Explorer shows; the invariant culture is
    /// explicit although every project already runs with InvariantGlobalization.</summary>
    public IReadOnlyList<string> Sentences => Loaders.Select(l =>
        $"{l.Name} ({Path.GetFileName(l.LoaderPath)}, {Day(l.LoaderUtc)}) is older than the game's executable "
        + $"({Path.GetFileName(GameExePath)}, {Day(GameExeUtc)}). {l.Name} usually needs a new release after "
        + "a game patch; if the game has been patched since you installed it, update it before launching"
        + (l.Url is null ? "." : $" ({l.Url}).")).ToList();

    /// <summary>The chip's text, or null when there is nothing to say: no stale loader, or already marked
    /// checked against this executable.</summary>
    public string? Summary(DateTime? checkedExeUtc)
        => Loaders.Count == 0 || CheckedAgainst(checkedExeUtc) ? null : string.Join(" ", Sentences);

    private static string Day(DateTime utc)
        => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
/// which build they load into (an ASI loader, ReShade, BepInEx's doorstop) stay out: flagging them after
/// every Steam patch would teach people to ignore the warning.</para>
///
/// <para><b>Recognised the way the rest of the launcher recognises them.</b> REFramework and Elden Mod
/// Loader by <see cref="ProxyLoaderRows"/>' proxy-plus-sibling pair, UE4SS by <see cref="FrameworkDeps"/>'
/// two components (the runtime's date, and only while the proxy that loads it is there: a stepped-aside or
/// half-removed UE4SS injects nothing). So a row, a FRAMEWORK chip and this one cannot name or link a
/// loader differently. Mod Engine 2 has no such entry for its DLL, so its files are named here, looked for
/// in the probe roots and in the folder its registered config sits in.</para>
///
/// <para><b>Dates, not versions.</b> None of these publish a "supports build N" fact the launcher could
/// read. The loader's file date is its release date: archive tools keep it on extraction, and so do the
/// launcher's own installs (<see cref="Frameworks.FrameworkInstaller"/>, <see cref="SharpCompressArchiveReader"/>).
/// The executable's is when Steam last wrote it. So the check says what it knows: the loader is older than
/// the game binary.</para>
/// </summary>
public static class StaleLoaders
{
    /// <summary>The loader must be older than the executable by more than this. A loader and a game set up
    /// the same day are not a mismatch worth a chip.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromDays(1);

    /// <summary>Executables within this fraction of the largest one's size count as the same binary; the
    /// newest of them is the build. The anti-cheat swap leaves a COPY of the game exe under another name,
    /// with the copy's old date, and a patch need not touch it.</summary>
    private const double SameBinary = 0.9;

    /// <summary>A loader this check knows: any one of its files, plus (when the file alone is ambiguous or
    /// inert) one of the files that must be beside it in the same root.</summary>
    private sealed record VersionLocked(
        string Name, IReadOnlyList<string> AnyOf, IReadOnlyList<string> AlongsideAnyOf, string? Url,
        bool InModEngineFolder = false);

    private static readonly IReadOnlyList<VersionLocked> Catalog = BuildCatalog();

    private static IReadOnlyList<VersionLocked> BuildCatalog()
    {
        var list = new List<VersionLocked>();
        foreach (var name in new[] { "REFramework", "Elden Mod Loader" })
        {
            var known = ProxyLoaderRows.Known.Single(k => k.Name == name);
            list.Add(new VersionLocked(known.Name, new[] { known.Proxy }, new[] { known.Sibling }, known.Url));
        }

        var ue4ss = FrameworkDeps.Catalog.Single(d => d.Name == "UE4SS");
        list.Add(new VersionLocked(ue4ss.Name,
            ue4ss.Components.Single(c => c.Name == "runtime").AnyOf,
            ue4ss.Components.Single(c => c.Name == "loader").AnyOf,
            ue4ss.GetUrl));

        // The DLL is what hooks the game; the launcher exe is the fallback when it is laid out differently.
        var me2 = FrameworkDeps.Catalog.Single(d => d.Name == "Mod Engine 2");
        list.Add(new VersionLocked(me2.Name, new[] { "modengine2/bin/modengine2.dll", ModEngine2.LauncherExe },
            Array.Empty<string>(), me2.GetUrl, InModEngineFolder: true));
        return list;
    }

    /// <summary>
    /// The executable taken as the game's build, from a probe root or its <c>Binaries/Win64</c> (where a UE
    /// game's <c>-Shipping.exe</c> lives): the largest, or, among those within 10% of the largest, the
    /// newest. Null when there is none.
    ///
    /// <para>Largest, because the registration has no reliable "game exe" field: most Steam games only
    /// store a <c>steam://</c> target, and an exe target is often a mod launcher. Beside the real binary sit
    /// a UE bootstrap stub, crash reporters and mod launchers, all a fraction of its size.</para>
    /// </summary>
    public static (string Path, DateTime Utc)? GameExecutable(GameContext ctx)
        => GameExecutable(FrameworkDeps.ResolveProbeRoots(ctx));

    private static (string Path, DateTime Utc)? GameExecutable(IReadOnlyList<string> roots)
    {
        var exes = new List<FileInfo>();
        foreach (var root in roots)
            foreach (var dir in new[] { root, Path.Combine(root, "Binaries", "Win64") })
            {
                try { if (Directory.Exists(dir)) exes.AddRange(new DirectoryInfo(dir).EnumerateFiles("*.exe")); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        if (exes.Count == 0) return null;

        var largest = exes.Max(f => f.Length);
        var build = exes.Where(f => f.Length >= largest * SameBinary)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .First();
        return (build.FullName, build.LastWriteTimeUtc);
    }

    /// <summary>Every live version-locked loader older than the game's executable. A file read per loader
    /// and a directory listing per probe root; callers on a UI thread run it in the background.</summary>
    public static StaleLoaderReport Find(GameContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.GameRoot) || !Directory.Exists(ctx.GameRoot)) return StaleLoaderReport.None;
        var roots = FrameworkDeps.ResolveProbeRoots(ctx);
        if (GameExecutable(roots) is not { } exe) return StaleLoaderReport.None;

        // Mod Engine 2 is usually extracted into a folder of its own (ModEngine-2.x\), which LaunchScan finds
        // and records as the folder of the config it registers.
        var me2Folder = string.IsNullOrEmpty(ctx.Game.ModEngineConfig) ? null : Path.GetDirectoryName(ctx.Game.ModEngineConfig);

        var found = new List<StaleLoader>();
        foreach (var loader in Catalog)
        {
            var where = loader.InModEngineFolder && me2Folder is not null ? roots.Append(me2Folder).ToList() : roots;
            if (FileFor(loader, where) is not { } file) continue;
            DateTime loaderUtc;
            try { loaderUtc = File.GetLastWriteTimeUtc(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (exe.Utc - loaderUtc > Slack)
                found.Add(new StaleLoader(loader.Name, file, loaderUtc, loader.Url));
        }
        return new StaleLoaderReport(exe.Path, exe.Utc, found);
    }

    private static string? FileFor(VersionLocked loader, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (loader.AlongsideAnyOf.Count > 0 && !loader.AlongsideAnyOf.Any(rel => Exists(Combine(root, rel)))) continue;
            foreach (var rel in loader.AnyOf)
            {
                var p = Combine(root, rel);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static string Combine(string root, string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

    private static bool Exists(string p)
    {
        try { return File.Exists(p) || Directory.Exists(p); }
        catch { return false; }
    }
}
