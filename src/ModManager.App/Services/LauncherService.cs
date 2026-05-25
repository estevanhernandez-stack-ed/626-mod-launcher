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

    /// <summary>Persist a game's auto-backup-before-launch preference + retention count.</summary>
    public void SetAutoBackup(string gameId, bool onLaunch, int? keepAuto)
    {
        var reg = LoadRegistry();
        var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
        if (g is null) return;
        g.AutoBackupOnLaunch = onLaunch;
        g.SaveAutoKeep = keepAuto;
        SaveRegistry(reg);
    }

    /// <summary>Assemble a game entry from wizard input, persist it, and make it active.</summary>
    public GameEntry AddGame(GameInput input)
    {
        var reg = LoadRegistry();
        var entry = EnginePresets.BuildGameEntry(input, reg.Games.Select(g => g.Id));
        ApplyDetection(entry);
        reg = Registry.UpsertGame(reg, entry);
        reg.ActiveGameId = entry.Id; // a newly added game becomes active
        SaveRegistry(reg);
        return entry; // save folder is detected (Ludusavi-first) by the caller, async
    }

    /// <summary>Re-run mod-location + launcher detection for an existing game (e.g. after Mod
    /// Engine 2 is installed, or for a game added before detection existed). Persists + returns it.</summary>
    public GameEntry? Redetect(string gameId)
    {
        var reg = LoadRegistry();
        var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
        if (g is null) return null;
        ApplyDetection(g);
        SaveRegistry(reg);
        return g;
    }

    // Point a game at where its mods actually live (existing/sideloaded folders, or the correct
    // Unreal project subfolder) and at how to launch with mods (Mod Engine 2 / Seamless Co-op).
    private static void ApplyDetection(GameEntry g)
    {
        var detected = ModLocator.Detect(g.GameRoot, g.Engine);
        if (detected.Count > 0) g.ModLocations = detected;

        var launch = LaunchScan.Detect(g.GameRoot, g.Engine, g.SteamAppId);
        if (launch.Targets.Count > 0) g.LaunchTargets = launch.Targets;
        if (launch.ModEngineConfig is not null) g.ModEngineConfig = launch.ModEngineConfig;
    }

    /// <summary>The launch target run by the primary Launch button (explicit default, else first).</summary>
    public static LaunchTarget? DefaultTarget(GameEntry game)
        => game.LaunchTargets.FirstOrDefault(t => t.IsDefault) ?? game.LaunchTargets.FirstOrDefault();

    /// <summary>Launch the game's default target; falls back to the legacy steam:// / exe fields.</summary>
    public bool Launch(GameEntry game)
    {
        var target = DefaultTarget(game);
        if (target is not null) return Launch(target, game.GameRoot);

        var legacy = game.LaunchUrl ?? (string.IsNullOrEmpty(game.SteamAppId) ? null : $"steam://rungameid/{game.SteamAppId}");
        if (legacy is not null) { Open(legacy); return true; }
        if (!string.IsNullOrEmpty(game.LaunchExe))
        {
            var exe = Path.IsPathRooted(game.LaunchExe) ? game.LaunchExe : Path.Combine(game.GameRoot, game.LaunchExe);
            Open(exe);
            return true;
        }
        return false;
    }

    /// <summary>Run a specific launch target — exe with args + working dir, or a steam:// url.</summary>
    public bool Launch(LaunchTarget target, string? gameRoot = null)
    {
        var exe = target.Kind == "exe" && !Path.IsPathRooted(target.Target) && !string.IsNullOrEmpty(gameRoot)
            ? Path.Combine(gameRoot, target.Target)
            : target.Target;
        var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
        if (!string.IsNullOrEmpty(target.Args)) psi.Arguments = target.Args;
        if (!string.IsNullOrEmpty(target.WorkingDir)) psi.WorkingDirectory = target.WorkingDir;
        Process.Start(psi);
        return true;
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
