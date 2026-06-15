using System.Globalization;

namespace ModManager.Core;

/// <summary>Pure ordering for the installed-games picker: most-recently-played first (by the live
/// Steam lastPlayed timestamp), with never-played / unparseable games last, then alphabetical.</summary>
public static class InstalledGameSort
{
    public static IReadOnlyList<InstalledGame> RecentlyPlayedFirst(IReadOnlyList<InstalledGame> games)
        => games
            .OrderByDescending(g => long.TryParse(g.LastPlayed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : long.MinValue)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
