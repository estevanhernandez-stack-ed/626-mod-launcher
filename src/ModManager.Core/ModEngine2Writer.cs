using System.IO;

namespace ModManager.Core;

/// <summary>
/// Writes to a Mod Engine 2 config: enable/disable and load order straight back into <c>mods[]</c>, no
/// file moves. Moved out of the App's ModEngineService so the app and the agent-access MCP flip an ME2
/// mod through one path (see <see cref="ModToggle"/>). A one-time <c>.626bak</c> backup is taken before
/// the first edit, and every write goes through the atomic writer so a crash can't corrupt the config.
/// </summary>
public static class ModEngine2Writer
{
    public static void SetEnabled(GameEntry game, string name, bool enabled)
        => Edit(game, mods => mods.Select(m => m.Name == name ? m with { Enabled = enabled } : m).ToList());

    public static void SetAll(GameEntry game, bool enabled)
        => Edit(game, mods => mods.Select(m => m with { Enabled = enabled }).ToList());

    /// <summary>Reorder the mods array to match the given names; any unlisted mod is kept (never dropped).</summary>
    public static void Reorder(GameEntry game, IReadOnlyList<string> orderedNames)
        => Edit(game, mods =>
        {
            var byName = mods.ToDictionary(m => m.Name);
            var ordered = orderedNames.Where(byName.ContainsKey).Select(n => byName[n]).ToList();
            ordered.AddRange(mods.Where(m => !orderedNames.Contains(m.Name)));
            return ordered;
        });

    /// <summary>One-time backup so the user can always recover Mod Engine 2's original config.</summary>
    public static void BackupOnce(string configPath)
    {
        var bak = configPath + ".626bak";
        if (!File.Exists(bak)) { try { File.Copy(configPath, bak); } catch { /* best effort */ } }
    }

    private static void Edit(GameEntry game, Func<IReadOnlyList<Me2Mod>, IReadOnlyList<Me2Mod>> transform)
    {
        var path = game.ModEngineConfig;
        var toml = ModEngine2Listing.ReadConfig(game);
        if (path is null || toml is null) return;
        BackupOnce(path);
        var updated = ModEngine2Config.WriteMods(toml, transform(ModEngine2Config.ParseMods(toml)));
        AtomicJson.WriteTextAtomic(path, updated);
    }
}
