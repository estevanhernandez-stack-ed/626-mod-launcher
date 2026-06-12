namespace ModManager.Core.ConfigMods;

/// <summary>
/// Section-aware INI overlay for UE config-tweak mods. The mod's [Section] Key=Value pairs are
/// applied onto the existing file: plain keys replace (or add); UE array ops (+Key / -Key / .Key /
/// !Key) append — replacing them positionally would corrupt UE's list semantics. Sections the file
/// lacks are appended whole. Everything the mod doesn't mention — other keys, comments, blank
/// lines — survives untouched, and the existing file's newline style is preserved (the bare-CR
/// lesson). Pure string -> string; idempotent (re-merging the same mod adds nothing twice).
/// </summary>
public static class ConfigMerge
{
    public static string Merge(string existingIni, string modIni)
    {
        var nl = DetectNewline(existingIni) ?? DetectNewline(modIni) ?? "\r\n";
        var lines = SplitLines(existingIni);
        var modSections = ParseSections(modIni);

        foreach (var (section, entries) in modSections)
        {
            var headerIdx = FindSectionHeader(lines, section);
            if (headerIdx < 0)
            {
                if (lines.Count > 0 && lines[^1].Length != 0) lines.Add("");
                lines.Add("[" + section + "]");
                foreach (var e in entries) lines.Add(e.Raw);
                continue;
            }

            var endIdx = FindSectionEnd(lines, headerIdx);
            foreach (var e in entries)
            {
                if (e.IsArrayOp)
                {
                    if (!RangeContainsExact(lines, headerIdx + 1, endIdx, e.Raw))
                    { lines.Insert(endIdx, e.Raw); endIdx++; }
                }
                else
                {
                    var keyIdx = FindKeyInRange(lines, headerIdx + 1, endIdx, e.Key);
                    if (keyIdx >= 0) lines[keyIdx] = e.Raw;
                    else { lines.Insert(endIdx, e.Raw); endIdx++; }
                }
            }
        }

        return string.Join(nl, lines) + nl;
    }

    private static string? DetectNewline(string s)
        => s.Contains("\r\n") ? "\r\n" : s.Contains('\n') ? "\n" : null;

    private static List<string> SplitLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return new List<string>();
        var list = s.Replace("\r\n", "\n").Split('\n').ToList();
        if (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
        return list;
    }

    private sealed record ModEntry(string Raw, string Key, bool IsArrayOp);

    private static List<(string Section, List<ModEntry> Entries)> ParseSections(string modIni)
    {
        var result = new List<(string, List<ModEntry>)>();
        List<ModEntry>? current = null;
        foreach (var raw in SplitLines(modIni))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new List<ModEntry>();
                result.Add((line[1..^1].Trim(), current));
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0 || current is null) continue;
            var key = line[..eq].Trim();
            var isArray = key.Length > 0 && key[0] is '+' or '-' or '.' or '!';
            current.Add(new ModEntry(line, key, isArray));
        }
        return result;
    }

    private static int FindSectionHeader(List<string> lines, string section)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')
                && string.Equals(t[1..^1].Trim(), section, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>Index just past the section's last content line (before trailing blanks / next header).</summary>
    private static int FindSectionEnd(List<string> lines, int headerIdx)
    {
        var end = lines.Count;
        for (var i = headerIdx + 1; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[') && t.EndsWith(']')) { end = i; break; }
        }
        while (end > headerIdx + 1 && lines[end - 1].Trim().Length == 0) end--;
        return end;
    }

    private static bool RangeContainsExact(List<string> lines, int start, int end, string raw)
    {
        for (var i = start; i < end && i < lines.Count; i++)
            if (string.Equals(lines[i].Trim(), raw, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int FindKeyInRange(List<string> lines, int start, int end, string key)
    {
        for (var i = start; i < end && i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith(';') || t.StartsWith('#')) continue;
            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            var k = t[..eq].Trim();
            if (k.Length > 0 && k[0] is '+' or '-' or '.' or '!') continue; // array lines never key-match
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}
