using System.Globalization;
using System.Text.Json;

namespace ModManager.Core.Characters;

/// <summary>
/// Reads the characters out of a Cyberpunk 2077 save folder.
///
/// <para><b>The easy case, and worth having as the second implementation.</b> Each save is a folder
/// holding <c>metadata.9.json</c> (plain UTF-8 JSON, ~60 fields), <c>sav.dat</c> (chunked LZ4 — never
/// touched here) and <c>screenshot.png</c> (456x256, free preview art). Nothing needs decoding, and
/// nothing here writes.</para>
///
/// <para><b>One character per save, not one save per character.</b> Unlike Elden Ring's ten slots in a
/// single file, Cyberpunk writes a folder per save point — 93 on the machine this was written against,
/// across two playthroughs. <c>playthroughID</c> is the character key; the folder is a moment in that
/// character's life. Grouping by it is what turns 93 rows into 2.</para>
///
/// <para>Traps handled, each found on real data: the metadata filename carries a format version so it
/// is globbed rather than named; two of 93 folders have <b>no <c>sav.dat</c> at all</b> and would throw
/// a scanner that assumes the triple; and <c>timestampString</c> is <c>HH:mm:ss, d.MM.yyyy</c> —
/// day-first with a <b>non-padded day</b>, so <c>DateTime.Parse</c> silently reads 8 July as 7 August
/// under a US locale.</para>
/// </summary>
public static class CyberpunkCharacters
{
    public const string MetadataGlob = "metadata.*.json";
    public const string PayloadName = "sav.dat";
    public const string ThumbnailName = "screenshot.png";

    /// <summary>What a save with no payload is tagged with. Two of ninety-three real folders.</summary>
    internal const string IncompleteMark = "incomplete";

    /// <summary>Cyberpunk's own format. Explicit, invariant, and day-first — never
    /// <c>DateTime.Parse</c>, which reads <c>8.07.2025</c> as 7 August in a US locale.</summary>
    private const string TimestampFormat = "HH:mm:ss, d.MM.yyyy";

    /// <summary>
    /// Every save in the folder, newest first. Never throws — an unreadable save is skipped, because
    /// one corrupt folder must not cost the user the list of the other ninety-two.
    /// </summary>
    public static IReadOnlyList<SaveCharacter> Read(string saveDir)
    {
        if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
            return Array.Empty<SaveCharacter>();

        var found = new List<SaveCharacter>();
        string[] dirs;
        try { dirs = Directory.GetDirectories(saveDir); } catch { return Array.Empty<SaveCharacter>(); }

        foreach (var dir in dirs)
        {
            var one = ReadOne(dir);
            if (one is not null) found.Add(one);
        }

        return found.OrderByDescending(c => c.LastPlayed ?? DateTime.MinValue).ToList();
    }

    /// <summary>One save folder, or null when it holds no readable metadata.</summary>
    public static SaveCharacter? ReadOne(string saveFolder)
    {
        try
        {
            var meta = Directory.GetFiles(saveFolder, MetadataGlob).FirstOrDefault();
            if (meta is null) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(meta));
            if (!doc.RootElement.TryGetProperty("Data", out var data)) return null;
            if (!data.TryGetProperty("metadata", out var m)) return null;

            var folder = Path.GetFileName(saveFolder.TrimEnd(Path.DirectorySeparatorChar,
                                                             Path.AltDirectorySeparatorChar));

            // The folder name is what the game's load list shows, and renaming a folder renames the
            // save in-game. `name` inside the metadata is only ever a mirror of it, written once.
            var name = folder.Length > 0 ? folder : Str(m, "name");

            var level = Num(m, "level");
            var cred = Num(m, "streetCred");
            var progress = level is null
                ? ""
                : $"Level {level}" + (cred is null ? "" : $"  ·  Street Cred {cred}");

            // A save whose payload is missing is a real thing on a real machine - two of ninety-three.
            // Say so rather than offering it as if it could be loaded.
            var orphaned = !File.Exists(Path.Combine(saveFolder, PayloadName));
            if (orphaned) progress = progress.Length == 0 ? "Incomplete save" : progress + "  ·  incomplete";

            var shot = Path.Combine(saveFolder, ThumbnailName);

            return new SaveCharacter(
                Id: folder,
                Name: name,
                Kind: Str(m, "lifePath"),
                Progress: progress,
                LastPlayed: ParseTimestamp(Str(m, "timestampString")),
                ThumbnailPath: File.Exists(shot) ? shot : null,
                MadeWithMods: Bool(m, "isModded"),
                Editable: false);      // sav.dat is chunked LZ4 and we have no business writing it
        }
        catch { return null; }
    }

    /// <summary>
    /// The CHARACTERS, not the saves — one row per playthrough, newest first.
    ///
    /// <para>Ninety-three saves on the real install are two people. Listing every folder would be a
    /// wall of near-identical rows: <c>AutoSave-7</c>, <c>AutoSave-6</c>, <c>AutoSave-19</c>, all the
    /// same V at the same level, minutes apart. They are moments, not characters.</para>
    ///
    /// <para>Cyberpunk stores no character NAME — V is always V — so the row is identified by life
    /// path and progress instead, taken from that playthrough's newest save, with the save count
    /// carried in the detail line so nothing is hidden.</para>
    /// </summary>
    public static IReadOnlyList<SaveCharacter> ReadCharacters(string saveDir)
    {
        if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir))
            return Array.Empty<SaveCharacter>();

        string[] dirs;
        try { dirs = Directory.GetDirectories(saveDir); } catch { return Array.Empty<SaveCharacter>(); }

        var byPlaythrough = new Dictionary<string, List<SaveCharacter>>(StringComparer.Ordinal);
        foreach (var dir in dirs)
        {
            var one = ReadOne(dir);
            if (one is null) continue;
            // A save with no playthrough id stands alone rather than being lumped with the others -
            // guessing that two unidentified saves are the same person is exactly the mistake that
            // makes a list untrustworthy.
            var key = PlaythroughIdOf(dir) ?? "save:" + one.Id;
            if (!byPlaythrough.TryGetValue(key, out var list))
                byPlaythrough[key] = list = new List<SaveCharacter>();
            list.Add(one);
        }

        var characters = new List<SaveCharacter>();
        foreach (var (key, saves) in byPlaythrough)
        {
            var ordered = saves.OrderByDescending(s => s.LastPlayed ?? DateTime.MinValue).ToList();
            var broken = ordered.Count(s => s.Progress.Contains(IncompleteMark, StringComparison.Ordinal));

            // The character's level and date should come from a save that would actually LOAD. One
            // real playthrough here has an orphaned newest save, and reading its stats made the whole
            // character row say "incomplete" - describing a file while appearing to describe a person.
            var newest = ordered.FirstOrDefault(s => !s.Progress.Contains(IncompleteMark, StringComparison.Ordinal))
                         ?? ordered[0];

            var count = ordered.Count;
            var tally = $"{count} save{(count == 1 ? "" : "s")}"
                      + (broken == 0 ? "" : $" ({broken} incomplete)");
            var progress = newest.Progress.Replace("  ·  " + IncompleteMark, "").Replace(IncompleteMark, "").Trim();

            characters.Add(newest with
            {
                Id = key,
                Name = "V",                                    // the game names no one else
                Progress = progress.Length == 0 ? tally : $"{progress}  ·  {tally}",
            });
        }

        return characters.OrderByDescending(c => c.LastPlayed ?? DateTime.MinValue).ToList();
    }

    /// <summary>Which character a save belongs to. Shared by every save in one playthrough, so it is
    /// what turns ninety-three saves into two characters.</summary>
    public static string? PlaythroughIdOf(string saveFolder)
    {
        try
        {
            var meta = Directory.GetFiles(saveFolder, MetadataGlob).FirstOrDefault();
            if (meta is null) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(meta));
            return doc.RootElement.TryGetProperty("Data", out var d)
                && d.TryGetProperty("metadata", out var m)
                && m.TryGetProperty("playthroughID", out var p) ? p.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Cyberpunk's timestamp, parsed on its own terms.
    ///
    /// <para><c>"23:36:47, 8.07.2025"</c> — day-first, month padded, day NOT padded. A locale-sensitive
    /// parse reads that as 7 August on a US machine and is wrong by a month without ever failing.</para>
    /// </summary>
    public static DateTime? ParseTimestamp(string? value)
        => DateTime.TryParseExact(value, TimestampFormat, CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out var t) ? t : null;

    private static string Str(JsonElement m, string name)
        => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int? Num(JsonElement m, string name)
        => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : null;

    private static bool? Bool(JsonElement m, string name)
        => m.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;   // absent is NOT false - the flag only exists from patch 1.6 onward
}
