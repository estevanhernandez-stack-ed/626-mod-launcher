using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Treats a FromSoft game's Mod Engine 2 config as the source of truth for its mods. ME2's
/// <c>mods[]</c> array decides what loads and in what priority (earlier wins conflicts), so for
/// these games we read the list from the config and write enable/disable + load order straight
/// back — no file moves. A one-time <c>.626bak</c> backup is taken before the first edit, and
/// every write goes through the atomic writer so a crash can't corrupt the user's config.
/// </summary>
public sealed class ModEngineService
{
    public bool IsConfigBacked(GameEntry game) => ModEngine2Listing.IsConfigBacked(game);

    public void SetEnabled(GameEntry game, string name, bool enabled)
        => Edit(game, mods => mods.Select(m => m.Name == name ? m with { Enabled = enabled } : m).ToList());

    public void SetAll(GameEntry game, bool enabled)
        => Edit(game, mods => mods.Select(m => m with { Enabled = enabled }).ToList());

    /// <summary>Reorder the mods array to match the given names; any unlisted mod is kept (never dropped).</summary>
    public void Reorder(GameEntry game, IReadOnlyList<string> orderedNames)
        => Edit(game, mods =>
        {
            var byName = mods.ToDictionary(m => m.Name);
            var ordered = orderedNames.Where(byName.ContainsKey).Select(n => byName[n]).ToList();
            ordered.AddRange(mods.Where(m => !orderedNames.Contains(m.Name)));
            return ordered;
        });

    /// <summary>Uninstall: delete the mod's folder, then drop its config entry. Folder-first so a
    /// locked file (game running) leaves the config — and thus the mod — intact, error surfaced.</summary>
    public void Remove(GameEntry game, string name)
    {
        var path = game.ModEngineConfig;
        var toml = ReadConfig(game);
        if (path is null || toml is null) return;
        var mods = ModEngine2Config.ParseMods(toml);
        var target = mods.FirstOrDefault(m => m.Name == name);
        if (target is not null && !string.IsNullOrEmpty(target.Path))
        {
            var me2Dir = System.IO.Path.GetDirectoryName(path)!;
            var folder = System.IO.Path.IsPathRooted(target.Path) ? target.Path : System.IO.Path.Combine(me2Dir, target.Path);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); // may throw -> surfaced
        }
        Backup(path);
        AtomicJson.WriteTextAtomic(path, ModEngine2Config.WriteMods(toml, mods.Where(m => m.Name != name).ToList()));
    }

    private void Edit(GameEntry game, Func<IReadOnlyList<Me2Mod>, IReadOnlyList<Me2Mod>> transform)
    {
        var path = game.ModEngineConfig;
        var toml = ReadConfig(game);
        if (path is null || toml is null) return;
        Backup(path);
        var updated = ModEngine2Config.WriteMods(toml, transform(ModEngine2Config.ParseMods(toml)));
        AtomicJson.WriteTextAtomic(path, updated);
    }

    private static string? ReadConfig(GameEntry game) => ModEngine2Listing.ReadConfig(game);

    // One-time backup so the user can always recover Mod Engine 2's original config.
    private static void Backup(string path)
    {
        var bak = path + ".626bak";
        if (!File.Exists(bak)) { try { File.Copy(path, bak); } catch { /* best effort */ } }
    }
}
