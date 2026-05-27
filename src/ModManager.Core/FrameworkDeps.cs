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
            Name: "DLL proxy (dinput8/version/winhttp)",
            // Direct-inject mods chain off whichever DLL proxy is already installed. We check all three.
            DetectRelativePaths: new[]
            {
                "dinput8.dll",
                "version.dll",
                "winhttp.dll",
            },
            GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
            Note: "DLL proxy chain-loader for direct-inject mods (ELDEN MOD LOADER and similar). Most ER mods chain off dinput8.dll."),

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
}
