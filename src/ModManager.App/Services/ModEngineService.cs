using System.IO;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Treats a FromSoft game's Mod Engine 2 config as the source of truth for its mods. ME2's
/// <c>mods[]</c> array decides what loads and in what priority (earlier wins conflicts). Reading the
/// list lives in <see cref="ModManager.Core.ModEngine2Listing"/> and the config writes in
/// <see cref="ModManager.Core.ModEngine2Writer"/>, both shared with the agent-access MCP. This class
/// keeps the App's DI shape and owns the one write that also deletes a folder: uninstall.
/// </summary>
public sealed class ModEngineService
{
    public bool IsConfigBacked(GameEntry game) => ModEngine2Listing.IsConfigBacked(game);

    public void SetEnabled(GameEntry game, string name, bool enabled) => ModEngine2Writer.SetEnabled(game, name, enabled);

    public void SetAll(GameEntry game, bool enabled) => ModEngine2Writer.SetAll(game, enabled);

    /// <summary>Reorder the mods array to match the given names; any unlisted mod is kept (never dropped).</summary>
    public void Reorder(GameEntry game, IReadOnlyList<string> orderedNames) => ModEngine2Writer.Reorder(game, orderedNames);

    /// <summary>Uninstall: delete the mod's folder, then drop its config entry. Folder-first so a
    /// locked file (game running) leaves the config — and thus the mod — intact, error surfaced.</summary>
    public void Remove(GameEntry game, string name)
    {
        var path = game.ModEngineConfig;
        var toml = ModEngine2Listing.ReadConfig(game);
        if (path is null || toml is null) return;
        var mods = ModEngine2Config.ParseMods(toml);
        var target = mods.FirstOrDefault(m => m.Name == name);
        if (target is not null && !string.IsNullOrEmpty(target.Path))
        {
            var me2Dir = System.IO.Path.GetDirectoryName(path)!;
            var folder = System.IO.Path.IsPathRooted(target.Path) ? target.Path : System.IO.Path.Combine(me2Dir, target.Path);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); // may throw -> surfaced
        }
        ModEngine2Writer.BackupOnce(path);
        AtomicJson.WriteTextAtomic(path, ModEngine2Config.WriteMods(toml, mods.Where(m => m.Name != name).ToList()));
    }
}
