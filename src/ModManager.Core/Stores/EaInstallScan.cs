namespace ModManager.Core.Stores;

/// <summary>One EA install as the registry names it: <c>HKLM\SOFTWARE\&lt;publisher&gt;\&lt;title&gt;</c>,
/// values <c>DisplayName</c> and <c>Install Dir</c>.</summary>
public sealed record EaRegistryInstall(string? DisplayName, string? InstallDir);

/// <summary>
/// Turns registry entries into installed games. Pure: the App reads the registry and passes a file
/// reader. An entry counts only when its installerdata can be read and parses — a staged or partial
/// install is skipped. The same folder seen under both registry views is listed once.
/// </summary>
public static class EaInstallScan
{
    public const string StoreKind = "ea";

    public static IReadOnlyList<InstalledGame> Scan(IEnumerable<EaRegistryInstall> entries, Func<string, string?> readText)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InstalledGame>();
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.InstallDir)) continue;
            var dir = e.InstallDir.Trim().TrimEnd('\\', '/');
            if (dir.Length == 0 || !seen.Add(dir)) continue;

            string? text;
            try { text = readText(Path.Combine(dir, "__Installer", "installerdata.xml")); }
            catch { continue; }
            if (EaInstallerData.Parse(text) is not { } install) continue;

            var name = install.Title ?? e.DisplayName ?? Path.GetFileName(dir);
            result.Add(new InstalledGame(StoreKind, install.ContentIds[0], name, dir) { BuildId = install.GameVersion });
        }
        return result;
    }
}
