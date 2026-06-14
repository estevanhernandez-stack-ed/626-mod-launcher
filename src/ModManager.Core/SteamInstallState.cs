using System.Globalization;

namespace ModManager.Core;

/// <summary>
/// Decodes a Steam appmanifest <c>StateFlags</c> value far enough to tell "fully installed" from
/// "still installing". The installed-games list uses this to skip games that aren't fully installed
/// yet (mid first-download) so they aren't offered for modding before they exist on disk.
///
/// StateFlags is a bitmask; bit 4 is StateFullyInstalled. It STAYS SET on update-required games
/// (e.g. 6 = installed + update-required, 1542 = installed + several flags) — those are genuinely
/// installed and addable, so they are NOT hidden. Only a value with bit 4 clear (e.g. 1 = uninstalled,
/// 2 = update-required-not-installed, 1026 = downloading) is treated as not-yet-installed.
///
/// Default-includes on an absent or unparseable value — we never hide a real game on uncertainty.
/// Verified against a real library (2026-06-14): installed games read 4/6/1542, all bit-4-set.
/// </summary>
public static class SteamInstallState
{
    private const int StateFullyInstalled = 4;

    public static bool IsFullyInstalled(string? stateFlags)
        => !int.TryParse(stateFlags, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)
           || (flags & StateFullyInstalled) != 0;
}
