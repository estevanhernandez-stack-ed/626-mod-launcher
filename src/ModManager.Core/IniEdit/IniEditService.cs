namespace ModManager.Core.IniEdit;

/// <summary>
/// Snapshot-before-write for INI files. Contract mirrors the FromSoft save editor's
/// snapshot-first law: the new write is only attempted after the backup copy is durably
/// in place. If the snapshot fails, the original INI is untouched.
///
/// Backups live at <c>&lt;gameDataDir&gt;/.ini-history/&lt;modId&gt;/&lt;iniName&gt;.&lt;unixMs&gt;.bak</c>.
/// Retention: the most recent <see cref="MaxBackupsPerFile"/> per INI are kept; older
/// <c>.bak</c> files are pruned synchronously after a successful save.
///
/// Pure core — no Electron / WinRT / UI imports. Tested under <c>dotnet test</c>.
/// </summary>
public static class IniEditService
{
    public const int MaxBackupsPerFile = 10;

    public static void SaveWithBackup(string iniPath, string newContents, string gameDataDir, string modId)
    {
        var bakDir = Path.Combine(gameDataDir, ".ini-history", modId);
        Directory.CreateDirectory(bakDir);

        var iniName = Path.GetFileName(iniPath);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var bakPath = Path.Combine(bakDir, $"{iniName}.{timestamp}.bak");

        // Snapshot the current contents first. Atomic — temp + rename.
        var currentContents = File.Exists(iniPath) ? File.ReadAllText(iniPath) : "";
        var bakTmp = bakPath + ".tmp";
        File.WriteAllText(bakTmp, currentContents);
        File.Move(bakTmp, bakPath, overwrite: true);

        // Write new contents. Backup is durable above — if the new-write fails, the
        // original is the .bak we just wrote and the on-disk INI is whatever state
        // the failed move left it in (which Move's atomicity bounds to "old" or "new").
        var iniTmp = iniPath + ".tmp";
        File.WriteAllText(iniTmp, newContents);
        File.Move(iniTmp, iniPath, overwrite: true);

        // Prune older backups for THIS INI. OrderByDescending on the path puts the
        // newest timestamp first; Skip(10) selects the older ones to delete.
        var stale = Directory.GetFiles(bakDir, $"{iniName}.*.bak")
            .OrderByDescending(f => f)
            .Skip(MaxBackupsPerFile)
            .ToList();
        foreach (var path in stale)
        {
            try { File.Delete(path); } catch { /* swallow — retention is best-effort */ }
        }
    }

    public static string? RestorePrevious(string iniPath, string gameDataDir, string modId)
    {
        var bakDir = Path.Combine(gameDataDir, ".ini-history", modId);
        if (!Directory.Exists(bakDir)) return null;

        var iniName = Path.GetFileName(iniPath);
        var mostRecent = Directory.GetFiles(bakDir, $"{iniName}.*.bak")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        return mostRecent is null ? null : File.ReadAllText(mostRecent);
    }
}
