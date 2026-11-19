using System.Globalization;
using System.Text;

namespace ModManager.Core;

/// <summary>One live DLL mod whose own log says it failed to start after the game's last update.</summary>
/// <param name="RowName">The mod's row, as the list names it (<see cref="DirectInject.DetectLoaderMods"/>).</param>
/// <param name="LogPath">Its <c>log.txt</c>.</param>
/// <param name="LogUtc">When that log was last written: the run that failed.</param>
/// <param name="Line">The failure line, verbatim.</param>
public sealed record BrokenMod(string RowName, string LogPath, DateTime LogUtc, string Line);

/// <summary>
/// Patch Day: mods that broke on a game update, read from their own logs. Memory-scanning DLL mods (Elden Ring's TechieW
/// set: UltrawideFix, AdjustTheFov, SkipTheIntro and the rest) look for a byte pattern in the game's code, and a patch
/// moves the code. They then log "Could not find signature!" and pop it on every modded launch. After the 2026-11-04
/// Elden Ring patch all six did, and Patch Day only knew about stale loaders.
///
/// <para>Detection only, never a move: offering to set them aside is the caller's, through the same reversible toggle as
/// a row. A failure counts when its log was written after the game executable AND after the mod's own DLL, so a mod
/// updated since its failure is not flagged until it fails again. Matched on the failure line, never a mod name. The
/// log is read from its tail (the latest run), because these logs grow without bound: SkipTheIntro's was 11 MB.</para>
/// </summary>
public static class BrokenModLogs
{
    private const string Signature = "Could not find signature";
    private const int TailBytes = 64 * 1024;

    /// <summary>Every live DLL-loader mod in <paramref name="playFolder"/> (<c>mods\&lt;Mod&gt;.dll</c> with its
    /// <c>mods\&lt;Mod&gt;\log.txt</c>) whose latest run failed after the game executable's last write. Empty when the
    /// executable's date is unknown: without it a failure can't be tied to an update.</summary>
    public static IReadOnlyList<BrokenMod> Find(string? playFolder, DateTime? exeUtc)
    {
        var found = new List<BrokenMod>();
        if (exeUtc is not { } exe || string.IsNullOrEmpty(playFolder)) return found;
        var modsDir = Path.Combine(playFolder, DirectInject.ModsDir);
        if (!Directory.Exists(modsDir)) return found;
        string[] files, dirs;
        try
        {
            files = Directory.GetFiles(modsDir).Select(f => Path.GetFileName(f)!).ToArray();
            dirs = Directory.GetDirectories(modsDir).Select(d => Path.GetFileName(d)!).ToArray();
        }
        catch { return found; }

        foreach (var mod in DirectInject.DetectLoaderMods(files, dirs))
        {
            var dll = Path.Combine(playFolder, mod.Entries[0]);
            var log = Path.Combine(modsDir, Path.GetFileNameWithoutExtension(dll), "log.txt");
            try
            {
                if (!File.Exists(log)) continue;
                var logUtc = File.GetLastWriteTimeUtc(log);
                if (logUtc < exe || logUtc < File.GetLastWriteTimeUtc(dll)) continue;
                if (FailureLine(log) is { } line) found.Add(new BrokenMod(mod.Name, log, logUtc, line));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* unreadable: claim nothing */ }
        }
        return found;
    }

    /// <summary>The failure line of the log's LATEST run, or null. Reads only the tail. A run starts at the mod's
    /// "&lt;Mod&gt; > Activating &lt;Mod&gt;..." line (verbatim from the real logs), so only lines after the last one count:
    /// an old failure followed by a newer run that never logged one is not a failure. A tail with no start line is one
    /// long run, read whole.</summary>
    internal static string? FailureLine(string logPath)
    {
        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var take = (int)Math.Min(fs.Length, TailBytes);
        fs.Seek(-take, SeekOrigin.End);
        var buf = new byte[take];
        var read = 0;
        while (read < take && fs.Read(buf, read, take - read) is var n && n > 0) read += n;
        var lines = Encoding.UTF8.GetString(buf, 0, read).Split('\n');
        var start = Array.FindLastIndex(lines, l => l.Contains("> Activating ", StringComparison.OrdinalIgnoreCase));
        for (var i = lines.Length - 1; i > start; i--)
        {
            var l = lines[i].Trim();
            if (l.Contains(Signature, StringComparison.OrdinalIgnoreCase)) return l;
        }
        return null;
    }

    /// <summary>The state-strip sentence, or null when nothing failed.</summary>
    public static string? Summary(IReadOnlyList<BrokenMod> broken, DateTime? exeUtc)
    {
        if (broken.Count == 0) return null;
        var when = exeUtc is { } e ? " " + e.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture) : "";
        var names = string.Join(", ", broken.Select(b => b.RowName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        var count = broken.Count == 1 ? "1 mod" : $"{broken.Count} mods";
        return $"{count} failed to start after the game's{when} update: {names}. They usually need a new release from their authors.";
    }
}
