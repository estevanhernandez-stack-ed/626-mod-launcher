using System.Diagnostics;
using System.IO;
using ModManager.Core;
using ModManager.Core.Persistence;
using ModManager.Core.Recency;

namespace ModManager.App.Services;

/// <summary>
/// App-layer bridge to the pure Core: loads the games registry (shared with the Electron app
/// at %APPDATA%\ModManagerBuilder), resolves the active game context, and owns the two bits of
/// real integration the Core deliberately leaves out — registry IO location and game launch.
/// </summary>
public sealed class LauncherService
{
    public ICurseForgeClient CurseForge { get; }

    public LauncherService(ICurseForgeClient curseForge) => CurseForge = curseForge;

    public static string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder");

    /// <summary>Raised after the registry changes on disk (Safe Clear / Restore). The App subscribes
    /// to re-read games.json and repaint (marshal to the UI thread).</summary>
    public event Action? RegistryChanged;
    public void NotifyRegistryChanged() => RegistryChanged?.Invoke();

    public GameRegistry LoadRegistry() => RegistryStore.Load(DataRoot);

    /// <summary>Load, change and save games.json as one locked step (<see cref="RegistryStore.Update"/>).
    /// Every write goes through here; there is no unlocked save (A6).</summary>
    public void UpdateRegistry(Action<GameRegistry> change) => RegistryStore.Update(DataRoot, change);

    /// <inheritdoc cref="UpdateRegistry(Action{GameRegistry})"/>
    public T UpdateRegistry<T>(Func<GameRegistry, (GameRegistry Registry, T Result)> change) => RegistryStore.Update(DataRoot, change);

    public GameContext? ActiveContext()
    {
        var game = Registry.GetActiveGame(LoadRegistry());
        // The save folder the game USES, which can be the curated one rather than a stored guess
        // (SaveDirRefresh). Read time only: the registry keeps what it had.
        return game is null ? null : Scanner.GameContext(game, SaveLocator.EffectiveSaveDir(game));
    }

    public void SetActiveGame(string id) => UpdateRegistry(reg => (Registry.SetActiveGame(reg, id), 0));

    /// <summary>Drop a game from the launcher's registry (its files + data on disk are untouched).</summary>
    public void RemoveGame(string id) => UpdateRegistry(reg => (Registry.RemoveGame(reg, id), 0));

    /// <summary>Persist the configured save folder for a game (used by the save manager).</summary>
    /// <param name="userChosen">True when the user picked the folder (Saves, Change…). It is then marked
    /// <see cref="GameEntry.UserSetSaveDir"/>, so a curated folder never replaces it; a detected folder
    /// clears the mark, being nobody's choice.</param>
    public void SetSaveDir(string gameId, string saveDir, bool userChosen = false) => UpdateRegistry(reg =>
    {
        var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
        if (g is null) return;
        g.SaveDir = saveDir;
        var marks = (g.UserSet ?? Array.Empty<string>())
            .Where(m => !string.Equals(m, GameEntry.UserSetSaveDir, StringComparison.OrdinalIgnoreCase));
        g.UserSet = (userChosen ? marks.Append(GameEntry.UserSetSaveDir) : marks).ToList() is { Count: > 0 } kept ? kept : null;
    });

    /// <summary>Persist a game's auto-backup-before-launch preference + retention count.</summary>
    public void SetAutoBackup(string gameId, bool onLaunch, int? keepAuto) => UpdateRegistry(reg =>
    {
        var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
        if (g is null) return;
        g.AutoBackupOnLaunch = onLaunch;
        g.SaveAutoKeep = keepAuto;
    });

    /// <summary>Record the current Steam build as this game's "modded against" baseline. Used to set the
    /// baseline silently on first sight and to re-baseline when the user dismisses the update warning.</summary>
    public void SetSteamBuildBaseline(string gameId, string? buildId) => UpdateRegistry(reg =>
    {
        var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
        if (g is null) return;
        g.LastKnownSteamBuildId = buildId;
    });

    /// <summary>Assemble a game entry from wizard input, persist it, and make it active.
    /// <para>An install the registry already knows about is never added twice — it is switched to instead,
    /// with <paramref name="alreadyRegistered"/> true and the existing entry returned. Without that guard a
    /// repeat add built a fresh id (<c>windrose-2</c>, <c>windrose-3</c>) and quietly stole the active game;
    /// see <see cref="Registry.FindRegistered"/>. All four add lanes come through here, so this covers
    /// Steam quick-add, batch add, the manual form, and the library's not-added-yet list.</para></summary>
    public GameEntry AddGame(GameInput input, out bool alreadyRegistered)
    {
        // The already-registered check and the add are one locked step: two adds of one install racing
        // each other cannot both pass the check and register it twice.
        var (entry, existed) = UpdateRegistry(reg =>
        {
            var existing = Registry.FindRegistered(reg, input.GameRoot, input.SteamAppId);
            if (existing is not null)
            {
                reg.ActiveGameId = existing.Id;
                return (reg, (existing, true));
            }

            var added = EnginePresets.BuildGameEntry(input, reg.Games.Select(g => g.Id));
            ApplyDetection(added);
            reg = Registry.UpsertGame(reg, added);
            reg.ActiveGameId = added.Id; // a newly added game becomes active
            return (reg, (added, false));
        });

        alreadyRegistered = existed;
        if (!existed) SeedModFolder(entry);
        return entry; // save folder is detected (Ludusavi-first) by the caller, async
    }

    /// <summary>Create the declared mod folder when the manifest named it and it is not there yet
    /// (A20). The decision is Core's and pure (<see cref="ModFolderSeed.PathToCreate"/>) - only the
    /// write lives here, because a read path must never create anything and Scanner is a read path.
    ///
    /// <para>Best-effort by design: a game folder we cannot write to is a real situation (Program
    /// Files without elevation, a read-only mount), and the launcher already handles an absent mod
    /// folder gracefully. Failing the ADD over it would turn a cosmetic improvement into a blocker.</para></summary>
    private static void SeedModFolder(GameEntry entry)
    {
        var path = ModFolderSeed.PathToCreate(entry);
        if (path is null) return;
        try { Directory.CreateDirectory(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Re-run mod-location + launcher detection for an existing game (e.g. after Mod
    /// Engine 2 is installed, or for a game added before detection existed). Persists + returns it.</summary>
    public GameEntry? Redetect(string gameId) => UpdateRegistry(reg =>
    {
        var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
        if (g is not null) ApplyDetection(g);
        return (reg, g);
    });

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

    /// <summary>Record that 626 just launched this game: stamp <see cref="GameEntry.LastLaunchedUtc"/>
    /// on the registry and append a <see cref="LaunchLogEntry"/> to the own-launch log. Called after a
    /// successful launch — never touches the launch mechanism itself. Callers should wrap this in
    /// try/catch: a stamping failure is non-fatal (recency degrades to the Steam source).</summary>
    public void StampLaunch(string gameId)
    {
        var now = DateTime.UtcNow;
        var found = UpdateRegistry(reg =>
        {
            var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
            if (g is not null) g.LastLaunchedUtc = now;
            return (reg, g is not null);
        });
        if (!found) return;
        LaunchLog.Append(new LaunchLogEntry(gameId, now, null));
    }
}
