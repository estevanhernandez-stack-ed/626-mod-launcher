namespace ModManager.Core.RestorePoints;

/// <summary>Builds the off-boarding report from a captured GameArchive — SELF-CONTAINED (no live
/// registry / LaunchScan): the launch instructions come from the sealed LaunchTargets/RequiredLauncher/
/// EndState, so the sheet is correct even after the clear deleted games.json and moved launchers out.</summary>
public static class OffBoardingHydrator
{
    public static OffBoardingReport Hydrate(GameArchive ga, string restorePointPath)
        => new(
            GameName: ga.GameName,
            RestorePointPath: restorePointPath,
            LaunchLines: LaunchLinesFrom(ga),
            Frameworks: ga.Frameworks.Select(f => $"{f.DisplayName} (by {f.Author})").ToList(),
            Mods: ga.Mods.Select(m => new OffBoardingModLine(
                m.Name, m.SourceUrl, m.SourceConfidence, FormatDate(m.InstalledUtc))).ToList(),
            OwnedMods: ga.OwnedMods.Select(o => new OffBoardingOwnedMod(o.Name, o.ManagedBy)).ToList(),
            SaveLocation: ga.SaveLocation,
            SaveBackupCount: ga.SaveBackupCount,
            TurnedOffCount: TurnedOffCount(ga),
            HeldInDataDir: ga.TurnedOffByClear is null ? null : ga.DataDir,
            TurnOffSkips: ga.TurnOffSkipped,
            InDataFolder: TurnedOff(ga).Count(m => !IsProxy(m) && !IsConfig(m)),
            Proxies: TurnedOff(ga).Count(IsProxy),
            InConfig: TurnedOff(ga).Count(IsConfig),
            CopiedToRestorePoint: TurnedOff(ga).Count(m => !IsProxy(m) && !IsConfig(m)
                && (ga.HeldCopies ?? Array.Empty<HeldCopy>()).Any(h => string.Equals(h.Name, m.Name, StringComparison.OrdinalIgnoreCase))),
            KeptTurnedOff: IsVanilla(ga) && ga.TurnedOffByClear is not null,
            RemainderMoved: ga.VanillaRemainder?.Count ?? 0,
            StillInPlace: ga.LeftInPlace);

    private static bool IsVanilla(GameArchive ga) => string.Equals(ga.EndState, "vanilla", StringComparison.OrdinalIgnoreCase);

    /// <summary>True only when the clear can honestly say the game is vanilla: the mod folders were swept
    /// (a remainder record exists), nothing was left in place, no turn-off refused, and no other tool's mods
    /// remain. An archive from before the sweep never qualifies: its mod folders were never cleared.</summary>
    public static bool FullyVanilla(GameArchive ga)
        => IsVanilla(ga) && ga.VanillaRemainder is not null
           && (ga.LeftInPlace?.Count ?? 0) == 0
           && (ga.TurnOffSkipped?.Count ?? 0) == 0
           && ga.OwnedMods.Count == 0;

    private static bool IsProxy(ClearedMod m) => m.Location == ProxyLoaderRows.LocationTag;
    private static bool IsConfig(ClearedMod m) => m.Location == "mod engine 2";

    // The sealed set minus the turn-offs that refused (those are still active).
    private static IReadOnlyList<ClearedMod> TurnedOff(GameArchive ga)
    {
        if (ga.TurnedOffByClear is null) return Array.Empty<ClearedMod>();
        var refused = new HashSet<string>((ga.TurnOffSkipped ?? Array.Empty<ClearSkip>()).Select(s => s.Name),
            StringComparer.OrdinalIgnoreCase);
        return ga.TurnedOffByClear.Where(m => !refused.Contains(m.Name)).ToList();
    }

    // What actually went off: the sealed set minus the turn-offs that refused (those are still active).
    private static int TurnedOffCount(GameArchive ga)
    {
        if (ga.TurnedOffByClear is null) return 0;
        var refused = new HashSet<string>((ga.TurnOffSkipped ?? Array.Empty<ClearSkip>()).Select(s => s.Name),
            StringComparer.OrdinalIgnoreCase);
        return ga.TurnedOffByClear.Count(m => !refused.Contains(m.Name));
    }

    // Launch guidance reflects the POST-CLEAR state. Vanilla: mod launchers were moved out -> launch
    // normally. modsActive: launchers are still installed -> point at the (default) launch target.
    private static IReadOnlyList<string> LaunchLinesFrom(GameArchive ga)
    {
        var lines = new List<string>();
        if (IsVanilla(ga))
        {
            if (FullyVanilla(ga))
                lines.Add("Your game has been returned to vanilla — launch it the way you normally would (e.g. from Steam).");
            else if (ga.VanillaRemainder is not null)
                lines.Add(WhatHappened(ga));
            else
                lines.Add("626 turned off the mods it manages. Files it doesn't manage may still be in the game's mod folders, "
                    + "so the game may not be fully vanilla. Launch it the way you normally would (e.g. from Steam).");
            return lines;
        }
        // modsActive — mods + their launchers are still installed.
        var def = ga.LaunchTargets.FirstOrDefault(t => t.IsDefault) ?? ga.LaunchTargets.FirstOrDefault();
        if (def is not null && string.Equals(def.Kind, "exe", StringComparison.OrdinalIgnoreCase))
            lines.Add($"Your mods are still active. Launch with: {def.Label} — {def.Target}");
        else
            lines.Add("Your mods are still active. Launch the game the way you normally do.");
        if (!string.IsNullOrEmpty(ga.RequiredLauncher))
            lines.Add($"This game needs its mod launcher ({ga.RequiredLauncher}) while mods are installed — don't launch vanilla from Steam.");
        return lines;
    }

    /// <summary>
    /// The vanilla launch line when the game isn't fully vanilla, in counts: how many mods went off, how many
    /// are still active, how many leftover files went into the restore point. Never "turned off its mods"
    /// when it turned off none, or only some (round 5).
    /// </summary>
    public static string WhatHappened(GameArchive ga)
    {
        var off = TurnedOff(ga).Count;
        var notes = ga.LeftInPlace ?? Array.Empty<InPlaceNote>();
        // Mods still active: named mods 626 left on (it replaced a game file), and turn-offs that refused.
        // Everything else left on is an ITEM 626 can't tell from the game's own files (a row in Data, a base
        // pak): never called a mod, and each name counted once (review r5, m-c).
        var activeMods = notes.Where(n => n.Reason == RestorePointEngine.ReplacedGameFileNote).Select(n => n.Path)
            .Concat((ga.TurnOffSkipped ?? Array.Empty<ClearSkip>()).Select(s => s.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var items = notes.Where(n => n.Reason.StartsWith(RestorePointEngine.CantTellRowPrefix, StringComparison.Ordinal)
                                     || n.Reason == RestorePointEngine.BasePakRowNote)
            .Select(n => n.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var moved = ga.VanillaRemainder?.Count ?? 0;
        static string Mods(int n) => n == 1 ? "1 mod" : $"{n} mods";
        static string IsAre(int n) => n == 1 ? "is" : "are";

        var head = off == 0
            ? (activeMods == 0 ? "626 didn't turn off any mods here" : $"626 didn't turn off any mods here: {Mods(activeMods)} {IsAre(activeMods)} still active")
            : activeMods == 0
                ? $"626 turned off all {Mods(off)} it found"
                : $"626 turned off {off} of {off + activeMods} mods; {activeMods} {IsAre(activeMods)} still active";
        var sweep = moved > 0 ? $", and moved {moved} other file{(moved == 1 ? "" : "s")} from the mod folders into your restore point" : "";
        var unknown = items > 0
            ? $". {items} item{(items == 1 ? "" : "s")} 626 can't tell from the game's own files {IsAre(items)} still in place"
            : "";
        return head + sweep + unknown + ". Some files are still in place (listed under STILL IN PLACE), so the game may not be fully vanilla. "
            + "Launch it the way you normally would (e.g. from Steam).";
    }

    private static string? FormatDate(string? iso)
        => string.IsNullOrEmpty(iso) ? null
           : (DateTimeOffset.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
               ? d.ToString("yyyy-MM-dd") : null);
}
