using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// App-layer bridge to the pure Core: loads the games registry (shared with the Electron app
/// at %APPDATA%\ModManagerBuilder), resolves the active game context, and owns the two bits of
/// real integration the Core deliberately leaves out — registry IO location and game launch.
/// </summary>
public sealed class LauncherService
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ICurseForgeClient CurseForge { get; }

    public LauncherService(ICurseForgeClient curseForge) => CurseForge = curseForge;

    private static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder");

    private static string RegistryPath => Path.Combine(DataRoot, "games.json");

    public GameRegistry LoadRegistry()
    {
        try { return JsonSerializer.Deserialize<GameRegistry>(File.ReadAllText(RegistryPath), Json) ?? Registry.EmptyRegistry(); }
        catch { return Registry.EmptyRegistry(); }
    }

    public void SaveRegistry(GameRegistry reg)
    {
        Directory.CreateDirectory(DataRoot);
        AtomicJson.WriteJsonAtomic(RegistryPath, reg);
    }

    public GameContext? ActiveContext()
    {
        var game = Registry.GetActiveGame(LoadRegistry());
        return game is null ? null : Scanner.GameContext(game);
    }

    public void SetActiveGame(string id) => SaveRegistry(Registry.SetActiveGame(LoadRegistry(), id));

    /// <summary>Drop a game from the launcher's registry (its files + data on disk are untouched).</summary>
    public void RemoveGame(string id) => SaveRegistry(Registry.RemoveGame(LoadRegistry(), id));

    /// <summary>Persist the configured save folder for a game (used by the save manager).</summary>
    public void SetSaveDir(string gameId, string saveDir)
    {
        var reg = LoadRegistry();
        var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
        if (g is null) return;
        g.SaveDir = saveDir;
        SaveRegistry(reg);
    }

    /// <summary>Assemble a game entry from wizard input, persist it, and make it active.</summary>
    public GameEntry AddGame(GameInput input)
    {
        var reg = LoadRegistry();
        var entry = EnginePresets.BuildGameEntry(input, reg.Games.Select(g => g.Id));
        // Proactively find the save folder now, so it's ready before they ever open Saves.
        entry.SaveDir = SaveLocator.Detect(entry.GameName, entry.Engine, entry.GameRoot);
        reg = Registry.UpsertGame(reg, entry);
        reg.ActiveGameId = entry.Id; // a newly added game becomes active
        SaveRegistry(reg);
        return entry;
    }

    /// <summary>Launch the game via its configured target (steam:// url, then exe fallback).</summary>
    public bool Launch(GameEntry game)
    {
        var target = game.LaunchUrl ?? (string.IsNullOrEmpty(game.SteamAppId) ? null : $"steam://rungameid/{game.SteamAppId}");
        if (target is not null) { Open(target); return true; }
        if (!string.IsNullOrEmpty(game.LaunchExe))
        {
            var exe = Path.IsPathRooted(game.LaunchExe) ? game.LaunchExe : Path.Combine(game.GameRoot, game.LaunchExe);
            Open(exe);
            return true;
        }
        return false;
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
