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
