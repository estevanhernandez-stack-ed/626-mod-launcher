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

        // Write new contents, normalized to the file's original newline style (CRLF for a
        // new/empty/bare-CR file). A WinUI TextBox collapses every \r\n to a bare \r on
        // round-trip; writing that verbatim corrupts the file for line-based parsers (e.g.
        // Seamless Co-op's ersc_settings.ini, which then can't find its sections). Normalizing
        // here protects every caller, not just the dialog. Backup is durable above — if the
        // new-write fails, the original is the .bak we just wrote and the on-disk INI is
        // whatever state the failed move left it in (Move's atomicity bounds it to "old" or "new").
        var iniTmp = iniPath + ".tmp";
        File.WriteAllText(iniTmp, NormalizeNewlines(newContents, DetectNewline(currentContents)));
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

    /// <summary>The newline style to write back: preserve the file's existing convention
    /// (CRLF or LF), defaulting to CRLF for new / empty / bare-CR-only files (the Windows
    /// INI default). Driven off the ORIGINAL on-disk contents, not the incoming string.</summary>
    private static string DetectNewline(string original)
    {
        if (original.Contains("\r\n")) return "\r\n";
        if (original.Contains('\n')) return "\n";
        return "\r\n";
    }

    /// <summary>Collapse any mix of CRLF, bare CR, and LF to a single consistent newline.
    /// Bare CR is the corruption a WinUI TextBox introduces; this is what removes it.</summary>
    private static string NormalizeNewlines(string contents, string newline)
    {
        var lf = contents.Replace("\r\n", "\n").Replace("\r", "\n");
        return lf.Replace("\n", newline);
    }
}
