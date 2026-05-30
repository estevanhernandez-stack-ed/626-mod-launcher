using System.IO;

namespace ModManager.Core;

/// <summary>
/// Read-only Mod Engine 2 listing. ME2's <c>mods[]</c> array is the source of truth for a
/// config-backed FromSoft game; this reads it as the launcher's mod list. Extracted from the
/// App's ModEngineService so the App and the headless agent-access MCP read ME2 mods through one
/// path. Parsing lives in <see cref="ModEngine2Config"/>; this resolves + maps.
/// </summary>
public static class ModEngine2Listing
{
    public static bool IsConfigBacked(GameEntry game)
        => game.Engine == "fromsoft"
           && !string.IsNullOrEmpty(game.ModEngineConfig)
           && File.Exists(game.ModEngineConfig);

    /// <summary>The config's mods as the normal mod list (priority order preserved).</summary>
    public static IReadOnlyList<Mod> List(GameEntry game)
    {
        var toml = ReadConfig(game);
        if (toml is null) return Array.Empty<Mod>();
        return ModEngine2Config.ParseMods(toml)
            .Select(m => new Mod
            {
                Name = m.Name,
                Base = m.Name,
                Class = "both",
                Enabled = m.Enabled,
                IsFolder = true,
                Location = "mod engine 2",
                // ME2 path token (relative, from the config) — not a filesystem path. Enable/disable
                // for these rows goes through ModEngineService's config rewrite, not Scanner file moves.
                Files = new List<string> { m.Path },
            })
            .ToList();
    }

    public static string? ReadConfig(GameEntry game)
    {
        try { return game.ModEngineConfig is null ? null : File.ReadAllText(game.ModEngineConfig); }
        catch { return null; }
    }
}
