namespace ModManager.Core;

/// <summary>
/// One PIECE a framework needs on disk, and the filenames that would satisfy it. Paths within a
/// component are alternatives (OR) — a loader that legitimately ships under several proxy names.
/// Components within a framework are all required (AND) — a runtime and the proxy that loads it.
///
/// <para>The distinction is not academic. UE4SS ships its runtime at
/// <c>Binaries/Win64/ue4ss/UE4SS.dll</c> and its loader at <c>Binaries/Win64/dwmapi.dll</c>; the game
/// loads the proxy, the proxy loads the runtime. Treating those two as alternatives — which is what
/// one flat any-of list does — meant either half alone read as "framework present". Proven live on
/// 2026-08-17: with the runtime moved aside, 626 reported <c>27 of 27 enabled</c> and zero NEEDS
/// chips while the game refused to start with its own "Failed to load UE4SS.dll" dialog. Twelve mods
/// were dead and the launcher said everything was fine.</para>
///
/// <para>A false red is noise. A false green is a user who believes their mods are on.</para>
/// </summary>
/// <param name="Name">What this piece is called in a sentence — "runtime", "loader". Surfaced to the
/// user, so a half-installed framework reads "runtime present, loader missing" rather than a bare
/// "Missing: UE4SS". Empty for a single-component framework, where there is no half to name.</param>
/// <param name="AnyOf">Relative paths, any ONE of which satisfies this component.</param>
public sealed record FrameworkComponent(string Name, IReadOnlyList<string> AnyOf)
{
    public static FrameworkComponent Of(string name, params string[] anyOf) => new(name, anyOf);
}

/// <summary>
/// One framework dependency the launcher knows about: what engine it belongs to, the pieces it needs
/// on disk, and where the user can get it. Pure data — no probing logic here, see
/// <see cref="FrameworkDeps"/>.
/// </summary>
/// <param name="Engine">Engine key from <c>EnginePresets.Presets</c> (e.g. "ue-pak", "bepinex").</param>
/// <param name="Name">Display name as the user knows it (UE4SS, BepInEx, SMAPI, ME2, Forge/Fabric).</param>
/// <param name="Components">The pieces this framework needs. ALL must be satisfied; each is satisfied
/// by ANY of its paths. A single component reproduces the old flat any-of behaviour exactly.</param>
/// <param name="GetUrl">https URL where the user can get the framework. Single canonical link
/// per framework — vendor releases page, not a wiki tree.</param>
/// <param name="Note">One-sentence why-it-matters, surfaced in the status banner tooltip.</param>
public sealed record FrameworkDep(
    string Engine,
    string Name,
    IReadOnlyList<FrameworkComponent> Components,
    string GetUrl,
    string Note)
{
    /// <summary>The single-component form: one any-of list, no half worth naming. Kept as a real
    /// constructor rather than a migration so every entry that IS genuinely a set of alternatives
    /// stays written exactly as it was — the diff then shows only the entries whose meaning changed.</summary>
    public FrameworkDep(string Engine, string Name, IReadOnlyList<string> DetectRelativePaths,
        string GetUrl, string Note)
        : this(Engine, Name, new[] { new FrameworkComponent("", DetectRelativePaths) }, GetUrl, Note) { }

    /// <summary>Every path across every component, in declaration order. For callers that only want
    /// "which files does this framework put on disk" — it deliberately does NOT express the AND/OR
    /// structure, so never use it to decide presence. <see cref="FrameworkDeps.Check"/> does that.</summary>
    public IReadOnlyList<string> DetectRelativePaths => Components.SelectMany(c => c.AnyOf).ToList();
}

/// <summary>What a probe found for one framework: which of its pieces are on disk and which are not.
/// The partial case is the one worth naming — it is both the dangerous state and the one a user can
/// act on immediately, because they already have half of it.</summary>
public sealed record FrameworkPresence(
    FrameworkDep Dep,
    IReadOnlyList<FrameworkComponent> Present,
    IReadOnlyList<FrameworkComponent> Missing)
{
    public bool IsPresent => Missing.Count == 0;

    /// <summary>Some pieces there, some not — the state that used to read as fully present.</summary>
    public bool IsPartial => Missing.Count > 0 && Present.Count > 0;

    /// <summary>How to say it. "UE4SS — runtime present, loader missing" for a half-install; the bare
    /// framework name when it is missing outright, because there is nothing to qualify.</summary>
    public string Describe()
    {
        if (!IsPartial) return Dep.Name;
        var parts = Present.Select(c => $"{Label(c)} present").Concat(Missing.Select(c => $"{Label(c)} missing"));
        return $"{Dep.Name} — {string.Join(", ", parts)}";
    }

    private static string Label(FrameworkComponent c) => string.IsNullOrWhiteSpace(c.Name) ? "part" : c.Name;
}
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
            // TWO components, not two variants. The game loads dwmapi.dll, which loads UE4SS.dll.
            // Both are required, and reading them as alternatives is what let a half-install report
            // clean while the game refused to start (proven live 2026-08-17 — see FrameworkComponent).
            Components: new[]
            {
                FrameworkComponent.Of("runtime", "Binaries/Win64/ue4ss/UE4SS.dll"),
                FrameworkComponent.Of("loader", "Binaries/Win64/dwmapi.dll"),
            },
            GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",
            Note: "Required for Lua mods and Blueprint LogicMods paks. Plain content paks don't need it."),

        new FrameworkDep(
            Engine: "bepinex",
            Name: "BepInEx",
            // REVIEWED against the UE4SS finding, deliberately UNCHANGED. This has the same
            // runtime-plus-proxy shape, so it is probably two components — but Doorstop ships under
            // several proxy names depending on version and platform, so the loader group is not just
            // winhttp.dll, and we have no BepInEx install here to establish what it is. Splitting it
            // on the strength of the resemblance would trade a dangerous failure for an annoying one
            // and still be a guess. Split it against a real install, not from memory.
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
            // REVIEWED, deliberately UNCHANGED. These two are neither variants nor a chain: one is
            // the launcher executable, the other its config. A config alone is not ME2, so an AND
            // would be closer to right than an OR — but ME2 is also installed in layouts we have not
            // seen, and a wrong AND puts a red chip on a working install. Decide with one in hand.
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
            // since the user might have a DIFFERENT proxy installed that also satisfies direct-inject
            // mods' chain-load requirement: dinput8 / version / winhttp (generic proxies), AND
            // Seamless Co-op's ersc.dll (proven live — with EML absent, launching via Seamless loads
            // the same direct-inject mods). Match the actual proxy DLL, not the SeamlessCoop folder,
            // so a settings-only folder doesn't false-clear the "needs a loader" chip.
            DetectRelativePaths: new[]
            {
                "dinput8.dll",
                "version.dll",
                "winhttp.dll",
                "SeamlessCoop/ersc.dll",  // Seamless subfolder layout (current)
                "ersc.dll",               // Seamless loose-at-root layout (older)
            },
            GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
            Note: "Elden Mod Loader — DLL proxy that direct-inject ER mods chain through (dinput8.dll). Only needed if you run a direct-inject mod that doesn't bring its own proxy; Seamless Co-op and ReShade do."),

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
        => Check(ctx).Where(p => !p.IsPresent).Select(p => p.Dep).ToList();

    /// <summary>
    /// The full picture for every catalog entry that applies to this game: which pieces of each
    /// framework are on disk and which are not.
    ///
    /// <para>A framework is present when EVERY component is satisfied, and a component is satisfied by
    /// ANY of its paths. The old flat any-of rule is the special case where there is one component,
    /// which is why entries that are genuinely lists of alternatives did not have to change.</para>
    /// </summary>
    public static IReadOnlyList<FrameworkPresence> Check(GameContext ctx)
    {
        var engine = ctx.Game.Engine ?? "";
        var entries = Catalog.Where(d => d.Engine == engine).ToList();
        if (entries.Count == 0) return Array.Empty<FrameworkPresence>();

        var roots = ResolveProbeRoots(ctx);
        var result = new List<FrameworkPresence>();
        foreach (var dep in entries)
        {
            var present = new List<FrameworkComponent>();
            var missing = new List<FrameworkComponent>();
            foreach (var component in dep.Components)
                (IsAnyPathPresent(component.AnyOf, roots) ? present : missing).Add(component);
            result.Add(new FrameworkPresence(dep, present, missing));
        }
        return result;
    }

    // For UE-pak: the project subfolders extracted from each resolved primary mod-location path
    // (the first path segment of the relative form: "R5/Content/Paks/~mods" -> "R5"), plus the
    // bare game root as a fallback. For FromSoft: the play folder (<gameRoot>/Game) if that
    // subfolder exists (the exe lives there + DLL proxies must sit next to the exe), plus the
    // bare game root. For everything else: just the game root.
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
        else if (ctx.Game.Engine == "fromsoft")
        {
            var gameFolder = System.IO.Path.Combine(ctx.GameRoot, "Game");
            if (System.IO.Directory.Exists(gameFolder) && !roots.Contains(gameFolder))
                roots.Add(gameFolder);
        }
        if (!roots.Contains(ctx.GameRoot)) roots.Add(ctx.GameRoot);
        return roots;
    }

    // Pull the project subfolder from a UE mod-location path: "R5/Content/Paks/~mods" -> "R5".
    // A path that starts with "Content/" (root-level fallback) has no project subfolder.
    // internal so the UE4SS framework installer reuses the one true rule (detection + install can't drift).
    internal static string? ProjectSubfolder(string relPath)
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
