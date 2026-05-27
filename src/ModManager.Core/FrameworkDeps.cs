namespace ModManager.Core;

/// <summary>
/// One framework dependency the launcher knows about: what engine it belongs to, where it
/// installs on disk (relative to the game root or a UE project subfolder), and where the
/// user can get it. Pure data — no probing logic here, see <see cref="FrameworkDeps"/>.
/// </summary>
/// <param name="Engine">Engine key from <c>EnginePresets.Presets</c> (e.g. "ue-pak", "bepinex").</param>
/// <param name="Name">Display name as the user knows it (UE4SS, BepInEx, SMAPI, ME2, Forge/Fabric, dinput8 proxy).</param>
/// <param name="DetectRelativePaths">One or more relative file paths; if ANY exists under the resolved
/// candidate roots, the framework is considered present. Multiple paths cover loader variants
/// (e.g. UE4SS ships its loader as <c>dwmapi.dll</c> next to <c>UE4SS.dll</c>).</param>
/// <param name="GetUrl">https URL where the user can get the framework. Single canonical link
/// per framework — vendor releases page, not a wiki tree.</param>
/// <param name="Note">One-sentence why-it-matters, surfaced in the status banner tooltip.</param>
public sealed record FrameworkDep(
    string Engine,
    string Name,
    IReadOnlyList<string> DetectRelativePaths,
    string GetUrl,
    string Note);

/// <summary>
/// Static catalog of framework dependencies the launcher knows about. One entry per
/// (engine, framework) pair. Mirrors the spec table at
/// <c>docs/superpowers/specs/2026-05-26-mod-dependency-detection-design.md</c>. Add an entry
/// here when adding a new engine + framework; the probe and the UI pick it up automatically.
/// </summary>
public static class FrameworkDeps
{
    public static IReadOnlyList<FrameworkDep> Catalog { get; } = new[]
    {
        new FrameworkDep(
            Engine: "ue-pak",
            Name: "UE4SS",
            // UE4SS ships its loader as dwmapi.dll next to the ue4ss/ runtime under
            // <Project>/Binaries/Win64. EITHER path existing means the framework is present.
            DetectRelativePaths: new[]
            {
                "Binaries/Win64/ue4ss/UE4SS.dll",
                "Binaries/Win64/dwmapi.dll",
            },
            GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",
            Note: "Required for Lua mods and Blueprint LogicMods paks. Plain content paks don't need it."),

        new FrameworkDep(
            Engine: "bepinex",
            Name: "BepInEx",
            DetectRelativePaths: new[]
            {
                "BepInEx/core/BepInEx.dll",
                "winhttp.dll",
            },
            GetUrl: "https://github.com/BepInEx/BepInEx/releases",
            Note: "Unity plugin loader. Required for any .dll mod under BepInEx/plugins/."),

        new FrameworkDep(
            Engine: "smapi",
            Name: "SMAPI",
            DetectRelativePaths: new[]
            {
                "StardewModdingAPI.exe",
            },
            GetUrl: "https://smapi.io/",
            Note: "Stardew Valley mod loader. Required for any folder mod under Mods/ with a manifest.json."),

        new FrameworkDep(
            Engine: "fromsoft",
            Name: "Mod Engine 2",
            DetectRelativePaths: new[]
            {
                "modengine2_launcher.exe",
                "mod/config_eldenring.toml",
            },
            GetUrl: "https://github.com/soulsmods/ModEngine2/releases",
            Note: "FromSoft folder-based mod loader. Required for /mod folder mods; not needed for direct-inject loose files."),

        new FrameworkDep(
            Engine: "fromsoft",
            Name: "Elden Mod Loader",
            // The catalog name calls out Elden Mod Loader specifically — it's the loader most ER
            // mods chain through and the one the user is searching for. The DLL probes stay broad
            // (dinput8 / version / winhttp) since the user might have a different proxy installed
            // that also satisfies direct-inject mods' chain-load requirement.
            DetectRelativePaths: new[]
            {
                "dinput8.dll",
                "version.dll",
                "winhttp.dll",
            },
            GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
            Note: "Elden Mod Loader — DLL proxy that direct-inject ER mods chain through (dinput8.dll). Most ER mods need this."),

        new FrameworkDep(
            Engine: "minecraft",
            Name: "Forge or Fabric",
            DetectRelativePaths: new[]
            {
                "libraries/net/minecraftforge",
                "libraries/net/fabricmc",
            },
            GetUrl: "https://files.minecraftforge.net/",
            Note: "Minecraft mod loader. Forge OR Fabric — install whichever matches your modpack."),
    };

    /// <summary>
    /// Return the catalog entries that are NOT present on disk for the active engine.
    /// Empty = nothing missing. For UE-pak games, detect paths are probed under each
    /// mod-location's project subfolder (e.g. <c>R5/Binaries/Win64/ue4ss/UE4SS.dll</c>),
    /// then under the bare game root as a fallback. For non-UE engines, detect paths are
    /// resolved relative to the game root only.
    /// </summary>
    public static IReadOnlyList<FrameworkDep> CheckPresent(GameContext ctx)
    {
        var engine = ctx.Game.Engine ?? "";
        var entries = Catalog.Where(d => d.Engine == engine).ToList();
        if (entries.Count == 0) return Array.Empty<FrameworkDep>();

        var roots = ResolveProbeRoots(ctx);
        var missing = new List<FrameworkDep>();
        foreach (var dep in entries)
        {
            if (!IsAnyPathPresent(dep.DetectRelativePaths, roots))
                missing.Add(dep);
        }
        return missing;
    }

    // For UE-pak: the project subfolders extracted from each resolved primary mod-location path
    // (the first path segment of the relative form: "R5/Content/Paks/~mods" -> "R5"), plus the
    // bare game root as a fallback. For everything else: just the game root.
    private static IReadOnlyList<string> ResolveProbeRoots(GameContext ctx)
    {
        var roots = new List<string>();
        if (ctx.Game.Engine == "ue-pak")
        {
            foreach (var loc in ctx.Game.ModLocations)
            {
                var sub = ProjectSubfolder(loc.Path);
                if (sub is null) continue;
                var abs = System.IO.Path.Combine(ctx.GameRoot, sub);
                if (!roots.Contains(abs)) roots.Add(abs);
            }
        }
        if (!roots.Contains(ctx.GameRoot)) roots.Add(ctx.GameRoot);
        return roots;
    }

    // Pull the project subfolder from a UE mod-location path: "R5/Content/Paks/~mods" -> "R5".
    // A path that starts with "Content/" (root-level fallback) has no project subfolder.
    private static string? ProjectSubfolder(string relPath)
    {
        if (string.IsNullOrWhiteSpace(relPath)) return null;
        var norm = relPath.Replace('\\', '/').TrimStart('/');
        var first = norm.Split('/')[0];
        if (string.IsNullOrEmpty(first)) return null;
        if (string.Equals(first, "Content", StringComparison.OrdinalIgnoreCase)) return null;
        return first;
    }

    private static bool IsAnyPathPresent(IReadOnlyList<string> relPaths, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
            foreach (var rel in relPaths)
                if (PathExists(System.IO.Path.Combine(root, rel)))
                    return true;
        return false;
    }

    // File.Exists for files; Directory.Exists for directories (Forge/Fabric libraries/ subtrees).
    private static bool PathExists(string p)
    {
        try { return System.IO.File.Exists(p) || System.IO.Directory.Exists(p); }
        catch { return false; }
    }
}
