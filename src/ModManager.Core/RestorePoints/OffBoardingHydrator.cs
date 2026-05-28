namespace ModManager.Core.RestorePoints;

/// <summary>Builds the off-boarding report from a captured <see cref="GameArchive"/>. Pure — the App
/// supplies the launch lines (from LaunchScan, which is App-only) + the restore-point path.</summary>
public static class OffBoardingHydrator
{
    public static OffBoardingReport Hydrate(GameArchive ga, string restorePointPath, IReadOnlyList<string> launchLines)
        => new(
            GameName: ga.GameName,
            RestorePointPath: restorePointPath,
            LaunchLines: launchLines,
            Frameworks: ga.Frameworks.Select(f => $"{f.DisplayName} (by {f.Author})").ToList(),
            Mods: ga.Mods.Select(m => new OffBoardingModLine(
                m.Name, m.SourceUrl, m.SourceConfidence, FormatDate(m.InstalledUtc))).ToList(),
            OwnedMods: ga.OwnedMods.Select(o => new OffBoardingOwnedMod(o.Name, o.ManagedBy)).ToList());

    // ISO-8601 -> yyyy-MM-dd, or null. RoundtripKind so a trailing "Z" parses correctly.
    private static string? FormatDate(string? iso)
        => string.IsNullOrEmpty(iso) ? null
           : (DateTimeOffset.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
               ? d.ToString("yyyy-MM-dd") : null);
}
