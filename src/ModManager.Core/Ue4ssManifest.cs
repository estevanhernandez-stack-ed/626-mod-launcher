using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Reads and writes UE4SS's own mod manifests so the launcher shows the TRUE enabled state and,
/// where we own the folder, drives it without moving files. Pure System.IO.
///
/// Rules (re-UE4SS): mods.txt / mods.json list "Name : 1|0" with file/array order = load order;
/// an empty 'enabled.txt' in a mod folder force-enables it irrespective of the manifest.
/// Effective enabled = (enabled.txt present) OR (manifest entry == true).
/// </summary>
public static class Ue4ssManifest
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Regex TxtLine = new(@"^\s*([^;:\s][^:]*?)\s*:\s*([01])\s*$", RegexOptions.Compiled);

    public static bool IsUe4ssFolder(string modsDir)
        => File.Exists(Path.Combine(modsDir, "mods.txt")) || File.Exists(Path.Combine(modsDir, "mods.json"));

    /// <summary>Manifest enable flags by mod name (mods.json preferred, else mods.txt). Order-preserving.</summary>
    private static List<(string Name, bool Enabled)> ReadManifest(string modsDir)
    {
        var json = Path.Combine(modsDir, "mods.json");
        if (File.Exists(json))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                var list = new List<(string, bool)>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var name = el.TryGetProperty("mod_name", out var n) ? n.GetString() : null;
                    var en = el.TryGetProperty("mod_enabled", out var e) && e.ValueKind == JsonValueKind.True;
                    if (!string.IsNullOrEmpty(name)) list.Add((name!, en));
                }
                return list;
            }
            catch { /* fall through to mods.txt */ }
        }
        var txt = Path.Combine(modsDir, "mods.txt");
        if (File.Exists(txt))
        {
            var list = new List<(string, bool)>();
            foreach (var raw in File.ReadAllLines(txt))
            {
                var m = TxtLine.Match(raw);
                if (m.Success) list.Add((m.Groups[1].Value.Trim(), m.Groups[2].Value == "1"));
            }
            return list;
        }
        return new List<(string, bool)>();
    }

    private static bool HasEnabledTxt(string modsDir, string name)
        => File.Exists(Path.Combine(modsDir, name, "enabled.txt"));

    /// <summary>Effective enabled for one mod folder: enabled.txt overrides; else the manifest flag; else false.</summary>
    public static bool IsEnabled(string modsDir, string name)
    {
        if (HasEnabledTxt(modsDir, name)) return true;
        foreach (var (n, en) in ReadManifest(modsDir))
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return en;
        return false;
    }

    /// <summary>
    /// Enable/disable a UE4SS mod WITHOUT moving files: set the flag in every present manifest
    /// (add the entry if missing), and on disable remove any enabled.txt (else it overrides the 0).
    /// Atomic writes. Transactional: if the second write fails, the first is restored from its
    /// pre-image so the two manifests never desync. No-op-safe if the folder has no manifest.
    /// </summary>
    public static void SetEnabled(string modsDir, string name, bool enabled)
    {
        var jsonPath = Path.Combine(modsDir, "mods.json");
        var txtPath  = Path.Combine(modsDir, "mods.txt");

        // Capture pre-images up front so we can roll back on partial failure.
        string? jsonPre = File.Exists(jsonPath) ? File.ReadAllText(jsonPath) : null;
        string? txtPre  = File.Exists(txtPath)  ? File.ReadAllText(txtPath)  : null;

        // Write mods.json FIRST (UE4SS prefers it when both exist; also our reader does).
        if (jsonPre is not null) WriteAtomic(jsonPath, SetInModsJson(jsonPre, name, enabled));

        try
        {
            if (txtPre is not null) WriteAtomic(txtPath, SetInModsTxt(File.ReadAllLines(txtPath), name, enabled));
        }
        catch
        {
            // Second write failed — restore mods.json from its pre-image so both manifests agree.
            if (jsonPre is not null) File.WriteAllText(jsonPath, jsonPre);
            throw;
        }

        // enabled.txt overrides the manifest — it must go when disabling.
        // Only reached after both manifest writes succeeded.
        if (!enabled)
        {
            var et = Path.Combine(modsDir, name, "enabled.txt");
            if (File.Exists(et)) File.Delete(et);
        }
    }

    private static string SetInModsTxt(string[] lines, string name, bool enabled)
    {
        var flag = enabled ? "1" : "0";
        var outLines = new List<string>();
        var found = false;
        var insertAt = -1; // before the keybinds comment, if any
        for (var i = 0; i < lines.Length; i++)
        {
            var m = TxtLine.Match(lines[i]);
            if (m.Success && string.Equals(m.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                outLines.Add($"{name} : {flag}");
                found = true;
            }
            else
            {
                if (insertAt < 0 && lines[i].TrimStart().StartsWith(";") &&
                    lines[i].Contains("keybind", StringComparison.OrdinalIgnoreCase))
                    insertAt = outLines.Count;
                outLines.Add(lines[i]);
            }
        }
        if (!found)
        {
            if (insertAt >= 0) outLines.Insert(insertAt, $"{name} : {flag}");
            else outLines.Add($"{name} : {flag}");
        }
        return string.Join("\r\n", outLines) + "\r\n";
    }

    /// <summary>
    /// Patch <paramref name="content"/> so the entry whose mod_name matches <paramref name="name"/>
    /// has mod_enabled == <paramref name="enabled"/>. ALL other fields on EVERY entry are preserved
    /// verbatim (values round-trip; whitespace is re-serialized with WriteIndented).
    /// Uses JsonNode so unknown keys are carried through without reconstruction.
    /// </summary>
    private static string SetInModsJson(string content, string name, bool enabled)
    {
        JsonArray arr;
        try
        {
            arr = JsonNode.Parse(content)?.AsArray() ?? new JsonArray();
        }
        catch
        {
            arr = new JsonArray();
        }

        var found = false;
        foreach (var node in arr)
        {
            if (node is not JsonObject obj) continue;
            var n = obj["mod_name"]?.GetValue<string>();
            if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) continue;
            obj["mod_enabled"] = JsonValue.Create(enabled);
            found = true;
            break;
        }

        if (!found)
        {
            var entry = new JsonObject
            {
                ["mod_name"]    = JsonValue.Create(name),
                ["mod_enabled"] = JsonValue.Create(enabled)
            };
            arr.Add(entry);
        }

        return arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Reorder the manifest so <paramref name="orderedNames"/> come first (in that order); other
    /// listed mods keep their relative order after; Keybinds stays pinned last. Enable flags,
    /// comments, and unknown json fields are preserved; mods.txt + mods.json stay in lockstep.
    /// No-op-safe when a manifest is absent.
    /// </summary>
    public static void SetOrder(string modsDir, IReadOnlyList<string> orderedNames)
    {
        var txt  = Path.Combine(modsDir, "mods.txt");
        var json = Path.Combine(modsDir, "mods.json");
        var txtPre  = File.Exists(txt)  ? File.ReadAllText(txt)  : null;
        var jsonPre = File.Exists(json) ? File.ReadAllText(json) : null;
        if (txtPre is null && jsonPre is null) return;

        // json first (authoritative), then txt; restore json on a txt failure (lockstep).
        if (jsonPre is not null) WriteAtomic(json, OrderModsJson(jsonPre, orderedNames));
        if (txtPre is not null)
        {
            try { WriteAtomic(txt, OrderModsTxt(txtPre, orderedNames)); }
            catch
            {
                if (jsonPre is not null) File.WriteAllText(json, jsonPre);
                throw;
            }
        }
    }

    /// <summary>
    /// Reorder mods.txt content. Header (leading comment/blank lines before the first entry) is
    /// preserved on top. Entries are reordered: first orderedNames that exist (in that order), then
    /// remaining entries in their original order. Keybinds is always pinned last, with its
    /// preceding "; Built-in keybinds" comment kept attached above it.
    /// </summary>
    private static string OrderModsTxt(string content, IReadOnlyList<string> orderedNames)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');

        // Classify lines: find the index of the first entry line.
        var firstEntryIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (TxtLine.IsMatch(lines[i])) { firstEntryIdx = i; break; }
        }

        // Collect header (lines before the first entry), entry lines (with their index for ordering),
        // and the keybinds comment line (if any).
        var headerLines = firstEntryIdx < 0 ? lines.ToList() : lines.Take(firstEntryIdx).ToList();
        var entryLines = new List<(string Name, string Line)>();  // (mod name, original line)
        string? keybindsComment = null;
        string? keybindsLine    = null;

        for (var i = firstEntryIdx < 0 ? lines.Length : firstEntryIdx; i < lines.Length; i++)
        {
            var m = TxtLine.Match(lines[i]);
            if (m.Success)
            {
                var name = m.Groups[1].Value.Trim();
                if (string.Equals(name, "Keybinds", StringComparison.OrdinalIgnoreCase))
                    keybindsLine = lines[i];
                else
                    entryLines.Add((name, lines[i]));
            }
            else
            {
                // A non-entry, non-header line: detect the "Built-in keybinds" comment.
                // Only capture it as keybindsComment if we haven't seen it yet (first occurrence wins).
                if (keybindsComment is null && lines[i].TrimStart().StartsWith(";") &&
                    lines[i].Contains("keybind", StringComparison.OrdinalIgnoreCase))
                    keybindsComment = lines[i];
                // Blank lines and other comments between entries are intentionally dropped —
                // we reconstruct a clean body; the header block is already captured above.
            }
        }

        // Build reordered body: ordered first, then the rest (preserving relative order).
        var orderedSet = new HashSet<string>(orderedNames, StringComparer.OrdinalIgnoreCase);
        var ordered = orderedNames
            .Select(n => entryLines.FirstOrDefault(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase)))
            .Where(e => e.Line is not null)
            .ToList();
        var rest = entryLines
            .Where(e => !orderedSet.Contains(e.Name))
            .ToList();

        var outLines = new List<string>();
        outLines.AddRange(headerLines);
        foreach (var (_, line) in ordered) outLines.Add(line);
        foreach (var (_, line) in rest)    outLines.Add(line);
        if (keybindsComment is not null) outLines.Add(keybindsComment);
        if (keybindsLine    is not null) outLines.Add(keybindsLine);

        return string.Join("\r\n", outLines) + "\r\n";
    }

    /// <summary>
    /// Reorder mods.json content. Build a new array: matching mod_name objects in orderedNames
    /// order first (whole node moved, all fields preserved), then the rest in original order,
    /// Keybinds pinned last. Serialized indented.
    /// </summary>
    private static string OrderModsJson(string content, IReadOnlyList<string> orderedNames)
    {
        JsonArray arr;
        try { arr = JsonNode.Parse(content)?.AsArray() ?? new JsonArray(); }
        catch { arr = new JsonArray(); }

        // Index all nodes by name for fast lookup; track Keybinds separately.
        var byName = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        JsonNode? keybindsNode = null;
        var originalOrder = new List<JsonNode>();

        foreach (var node in arr)
        {
            if (node is null) continue;
            var name = node["mod_name"]?.GetValue<string>();
            if (string.Equals(name, "Keybinds", StringComparison.OrdinalIgnoreCase))
                keybindsNode = node;
            else
            {
                if (name is not null) byName[name] = node;
                originalOrder.Add(node);
            }
        }

        // Build reordered list: orderedNames first (that exist), then remaining, Keybinds last.
        var orderedSet = new HashSet<string>(orderedNames, StringComparer.OrdinalIgnoreCase);
        var result = new List<JsonNode?>();
        foreach (var n in orderedNames)
        {
            if (byName.TryGetValue(n, out var node)) result.Add(node);
        }
        foreach (var node in originalOrder)
        {
            var name = node["mod_name"]?.GetValue<string>();
            if (name is null || !orderedSet.Contains(name)) result.Add(node);
        }
        if (keybindsNode is not null) result.Add(keybindsNode);

        // Build a fresh JsonArray from the reordered nodes (deep-clone via re-parse to avoid
        // "node already has a parent" errors when re-inserting nodes from the original array).
        var newArr = new JsonArray();
        foreach (var node in result)
        {
            if (node is null) continue;
            newArr.Add(JsonNode.Parse(node.ToJsonString()));
        }
        return newArr.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // Fix 3: unique temp name + cleanup so a failed write leaves no debris.
    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
            tmp = null; // consumed — don't delete in finally
        }
        finally
        {
            if (tmp is not null && File.Exists(tmp)) try { File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
