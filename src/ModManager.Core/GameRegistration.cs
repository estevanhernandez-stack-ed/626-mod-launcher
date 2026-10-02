using ModManager.Core.Persistence;

namespace ModManager.Core;

/// <summary>
/// Adding a game and re-detecting one: where its mods actually live and how to launch it with mods.
/// The app's add lanes and its Re-scan, and the agent's register_game and intake, all come through
/// here (E1, fifth slice), so a game an agent adds is the game the app would have added.
///
/// <para>Detection walks the game folder, so it runs OUTSIDE the registry lock: holding games.json
/// for a slow drive would stall every other writer. Only the check, the id and the upsert take the
/// lock (<see cref="RegistryStore.Update"/>, A6).</para>
/// </summary>
public static class GameRegistration
{
    private sealed record Detection(IReadOnlyList<ModLocation> ModLocations, LaunchDetection Launch);

    private static Detection Detect(GameEntry g)
        => new(ModLocator.Detect(g.GameRoot, g.Engine), LaunchScan.Detect(g.GameRoot, g.Engine, g.SteamAppId));

    // Only assigns, so it can run inside the registry lock.
    private static void Apply(Detection d, GameEntry g)
    {
        if (d.ModLocations.Count > 0) g.ModLocations = d.ModLocations;
        if (d.Launch.Targets.Count > 0) g.LaunchTargets = d.Launch.Targets;
        if (d.Launch.ModEngineConfig is not null) g.ModEngineConfig = d.Launch.ModEngineConfig;
    }

    /// <summary>Assemble a game entry from the input, persist it, and make it active.
    /// <para>An install the registry already knows about is never added twice: it is switched to
    /// instead, with <paramref name="alreadyRegistered"/> true and the existing entry returned. Without
    /// that guard a repeat add built a fresh id (<c>windrose-2</c>) and quietly stole the active game
    /// (<see cref="Registry.FindRegistered"/>).</para></summary>
    public static GameEntry Add(string dataRoot, GameInput input, out bool alreadyRegistered)
    {
        var detection = Detect(EnginePresets.BuildGameEntry(input, Array.Empty<string>()));

        // The already-registered check and the add are one locked step: two adds of one install racing
        // each other cannot both pass the check and register it twice.
        var (entry, existed) = RegistryStore.Update(dataRoot, reg =>
        {
            var existing = Registry.FindRegistered(reg, input.GameRoot, input.SteamAppId);
            if (existing is not null)
            {
                reg.ActiveGameId = existing.Id;
                return (reg, (existing, true));
            }

            var added = EnginePresets.BuildGameEntry(input, reg.Games.Select(g => g.Id));
            Apply(detection, added);
            reg = Registry.UpsertGame(reg, added);
            reg.ActiveGameId = added.Id; // a newly added game becomes active
            return (reg, (added, false));
        });

        alreadyRegistered = existed;
        if (!existed) SeedModFolder(entry);
        return entry;
    }

    /// <summary>Re-run mod-location and launcher detection for an existing game (after Mod Engine 2
    /// or Seamless Co-op is installed, or for a game added before detection existed). Persists and
    /// returns it, or null when there is no such game.</summary>
    public static GameEntry? Redetect(string dataRoot, string gameId)
    {
        var before = RegistryStore.Load(dataRoot).Games.FirstOrDefault(x => x.Id == gameId);
        if (before is null) return null;
        var detection = Detect(before);
        // Applied to the entry as it is THEN, inside the lock, so a concurrent change to anything else is kept.
        return RegistryStore.Update(dataRoot, reg =>
        {
            var g = reg.Games.FirstOrDefault(x => x.Id == gameId);
            if (g is not null) Apply(detection, g);
            return (reg, g);
        });
    }

    /// <summary>Create the declared mod folder when the manifest named it and it is not there yet
    /// (A20). The decision is <see cref="ModFolderSeed.PathToCreate"/>'s.
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
}
