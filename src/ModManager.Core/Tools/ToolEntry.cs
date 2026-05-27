namespace ModManager.Core.Tools;

/// <summary>
/// One installed third-party tool registered with the launcher. Persisted to
/// <c>_626mods/&lt;game&gt;/tools.json</c> as camelCase JSON. Catalog-recognized tools have
/// <c>Source = "catalog"</c>; heuristically-installed tools have <c>Source = "user"</c>.
/// </summary>
/// <param name="ToolId">Stable id derived from the extracted folder name (kebab-case).</param>
/// <param name="DisplayName">Button label.</param>
/// <param name="InstallDir">Absolute path under <c>_626mods/&lt;game&gt;/tools/&lt;id&gt;/</c>.</param>
/// <param name="Runnable">Relative path inside InstallDir to the launch target (.exe / .bat / .ps1 / .cmd).</param>
/// <param name="EditsSaves">If true, the launcher snapshots the save folder before launching the tool.</param>
/// <param name="GetUrl">Optional "Get it here" link for the catalog chip when the tool is uninstalled.</param>
/// <param name="Source">"catalog" (known tool, pre-filled metadata) or "user" (heuristic install).</param>
public sealed record ToolEntry(
    string ToolId,
    string DisplayName,
    string InstallDir,
    string Runnable,
    bool EditsSaves,
    string? GetUrl,
    string Source);
