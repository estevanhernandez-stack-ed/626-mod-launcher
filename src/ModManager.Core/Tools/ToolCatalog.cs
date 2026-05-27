namespace ModManager.Core.Tools;

/// <summary>
/// One catalog-known tool. Distinct from <see cref="ToolEntry"/> — this is the TEMPLATE the
/// detector matches against during drop. When matched, ToolIntake produces a ToolEntry from
/// the KnownTool metadata + the extracted folder.
/// </summary>
/// <param name="ToolId">Stable kebab-case id (matches ToolEntry.ToolId once installed).</param>
/// <param name="DisplayName">Button label.</param>
/// <param name="Engine">Engine the tool applies to (e.g. "ue-pak", "fromsoft").</param>
/// <param name="SteamAppId">Steam App ID the tool applies to (e.g. "3041230" for Windrose).</param>
/// <param name="EditsSaves">If true, launcher snapshots saves before launching the tool.</param>
/// <param name="GetUrl">Nexus / vendor page URL for the "Get it here" chip when uninstalled.</param>
/// <param name="ZipFilenameHints">Case-insensitive substrings that match this tool's zip filename.</param>
/// <param name="ExpectedRunnableHints">Filenames the runnable surfacing should prefer.</param>
/// <param name="Author">Attribution string for honor-the-builders surfaces.</param>
public sealed record KnownTool(
    string ToolId,
    string DisplayName,
    string Engine,
    string SteamAppId,
    bool EditsSaves,
    string? GetUrl,
    IReadOnlyList<string> ZipFilenameHints,
    IReadOnlyList<string> ExpectedRunnableHints,
    string Author);

/// <summary>
/// Static catalog of third-party tools the launcher knows about. Mirrors
/// <c>ModManager.Core.FrameworkDeps.Catalog</c> in shape. Add an entry here when a new tool
/// becomes day-one-supported; the detector and UI pick it up automatically.
///
/// Day-one entries: WSE Save Editor + WSE Save Fix (Windrose, by RimmyCode / WSE Project).
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<KnownTool> Catalog { get; } = new[]
    {
        new KnownTool(
            ToolId: "wse-save-editor",
            DisplayName: "WSE Save Editor",
            Engine: "ue-pak",
            SteamAppId: "3041230",
            EditsSaves: true,
            GetUrl: "https://www.nexusmods.com/windrose/mods/153",
            ZipFilenameHints: new[]
            {
                "windrose-save-editor",
                "wse-save-editor",
                "wse_save_editor",
                "save editor",
            },
            ExpectedRunnableHints: new[]
            {
                "WSE_Save_Editor.exe",
                "Windrose_Save_Editor.exe",
                "Save_Editor.exe",
            },
            Author: "RimmyCode (WSE Project)"),

        new KnownTool(
            ToolId: "wse-save-fix",
            DisplayName: "WSE Save Fix",
            Engine: "ue-pak",
            SteamAppId: "3041230",
            EditsSaves: true,
            // Pinned 2026-05-27 via web search "WSE Save Fix" site:nexusmods.com — Save Fixer (BETA).
            GetUrl: "https://www.nexusmods.com/windrose/mods/267",
            ZipFilenameHints: new[]
            {
                "wse-save-fix",
                "wse_save_fix",
                "save fix",
                "save-fix",
                "save-fixer",
                "save_fixer",
            },
            ExpectedRunnableHints: new[]
            {
                "WSE_Save_Fix.exe",
                "WSE_Save_Fixer.exe",
                "Save_Fix.exe",
                "Save_Fixer.exe",
            },
            Author: "RimmyCode (WSE Project)"),
    };
}
