using System.Globalization;

namespace ModManager.Core;

/// <summary>
/// The per-game last-poll stamp that debounces the Nexus auto-check. Pure given a path — the App
/// supplies <c>%LOCALAPPDATA%\ModManagerBuilder\last-nexus-poll-&lt;gameId&gt;.txt</c> (mirrors the
/// update-check stamp). The timestamp is stored as a round-trip-kind UTC string; read/write are
/// tolerant (a missing or garbage file reads as null — "never polled").
/// </summary>
public static class NexusPollStamp
{
    /// <summary>
    /// True when the auto-check should run: never polled (<paramref name="lastUtc"/> null) or more
    /// than <paramref name="interval"/> has elapsed since the last poll. Exactly-at-interval is not
    /// yet due (strictly greater-than).
    /// </summary>
    public static bool ShouldPoll(DateTime? lastUtc, DateTime nowUtc, TimeSpan interval)
        => lastUtc is not { } last || nowUtc - last > interval;

    /// <summary>
    /// Read the stamp at <paramref name="path"/>, or null if the file is missing or unparseable.
    /// The returned value is UTC-kind. Never throws.
    /// </summary>
    public static DateTime? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
                return parsed.ToUniversalTime();
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Write <paramref name="whenUtc"/> to <paramref name="path"/> as a round-trip-kind UTC string,
    /// creating the parent directory if needed.
    /// </summary>
    public static void Write(string path, DateTime whenUtc)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, whenUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }
}
