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
            SaveBackupCount: ga.SaveBackupCount);

    // Launch guidance reflects the POST-CLEAR state. Vanilla: mod launchers were moved out -> launch
    // normally. modsActive: launchers are still installed -> point at the (default) launch target.
    private static IReadOnlyList<string> LaunchLinesFrom(GameArchive ga)
    {
        var lines = new List<string>();
        if (string.Equals(ga.EndState, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("Your game has been returned to vanilla — launch it the way you normally would (e.g. from Steam).");
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

    private static string? FormatDate(string? iso)
        => string.IsNullOrEmpty(iso) ? null
           : (DateTimeOffset.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
               ? d.ToString("yyyy-MM-dd") : null);
}
