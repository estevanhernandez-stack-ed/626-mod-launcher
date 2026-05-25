namespace ModManager.Core;

/// <summary>One editable config setting: its section (null for top-level), key, current value, and
/// the human description we lift from the comment lines immediately above it.</summary>
public sealed record ConfigEntry(string? Section, string Key, string Value, string Description);

/// <summary>
/// Round-trip-safe reader/editor for INI-style config (.ini / .cfg / config.txt): supports
/// '[Section]' headers, 'key = value', and '#'/';' comments. Editing changes only the target
/// value's bytes; comments, ordering, spacing, and every other line are preserved. Pure string ops;
/// file IO lives in <see cref="ReadFile"/> / <see cref="Discover"/> and the Scanner write wrapper.
/// </summary>
public static class ModConfig
{
    private static bool IsComment(string t) => t.StartsWith('#') || t.StartsWith(';');

    public static IReadOnlyList<ConfigEntry> Parse(string content)
    {
        var entries = new List<ConfigEntry>();
        string? section = null;
        var comments = new List<string>();
        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var t = raw.Trim();
            if (t.Length == 0) { comments.Clear(); continue; }
            if (IsComment(t)) { comments.Add(t.TrimStart('#', ';', ' ', '\t')); continue; }
            if (t.StartsWith('[') && t.EndsWith(']')) { section = t[1..^1].Trim(); comments.Clear(); continue; }
            var eq = raw.IndexOf('=');
            if (eq < 0) { comments.Clear(); continue; }
            var key = raw[..eq].Trim();
            var value = raw[(eq + 1)..].Trim();
            if (key.Length > 0) entries.Add(new ConfigEntry(section, key, value, string.Join(" ", comments)));
            comments.Clear();
        }
        return entries;
    }

    /// <summary>Set a key's value in the given section (null = top-level), preserving every other byte.
    /// If the key is absent it is appended (under the section header if one is named and present).</summary>
    public static string SetValue(string content, string? section, string key, string value)
    {
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        string? cur = null;
        var sectionEndIdx = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { cur = t[1..^1].Trim(); if (cur == section) sectionEndIdx = i; continue; }
            if (t.Length == 0 || IsComment(t)) continue;
            var eq = lines[i].IndexOf('=');
            if (eq < 0) continue;
            if (string.Equals(cur, section, StringComparison.Ordinal) &&
                string.Equals(lines[i][..eq].Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = lines[i][..(eq + 1)] + " " + value;   // keep "key =", replace RHS
                return string.Join(nl, lines);
            }
            if (cur == section) sectionEndIdx = i;
        }
        // not found -> append (after the section's last line if a section was named, else at end)
        var insert = section is not null && sectionEndIdx >= 0 ? sectionEndIdx + 1 : lines.Count;
        // trim a single trailing empty line so we don't grow blank lines unboundedly
        if (insert == lines.Count && lines.Count > 0 && lines[^1].Trim().Length == 0) insert = lines.Count - 1;
        lines.Insert(insert, $"{key} = {value}");
        return string.Join(nl, lines);
    }

    private static readonly string[] ConfigExts = { ".ini", ".cfg" };
    private static readonly string[] ConfigNames = { "config.txt", "settings.txt" };
    private static readonly HashSet<string> NotConfig =
        new(StringComparer.OrdinalIgnoreCase) { "mods.txt", "enabled.txt", "mods.json" };

    /// <summary>Config files directly inside a mod folder: *.ini / *.cfg plus known names; never the
    /// UE4SS manifest files. Returns absolute paths.</summary>
    public static IReadOnlyList<string> Discover(string modDir)
    {
        try
        {
            return Directory.GetFiles(modDir)
                .Where(p =>
                {
                    var name = Path.GetFileName(p);
                    if (NotConfig.Contains(name)) return false;
                    return ConfigExts.Contains(Path.GetExtension(p).ToLowerInvariant())
                        || ConfigNames.Contains(name.ToLowerInvariant());
                })
                .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    public static IReadOnlyList<ConfigEntry> ReadFile(string path)
    {
        try { return Parse(File.ReadAllText(path)); } catch { return Array.Empty<ConfigEntry>(); }
    }
}
