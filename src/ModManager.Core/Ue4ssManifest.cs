using System.Text.Json;
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
    /// Atomic writes. No-op-safe if the folder has no manifest.
    /// </summary>
    public static void SetEnabled(string modsDir, string name, bool enabled)
    {
        var txt = Path.Combine(modsDir, "mods.txt");
        if (File.Exists(txt)) WriteAtomic(txt, SetInModsTxt(File.ReadAllLines(txt), name, enabled));

        var json = Path.Combine(modsDir, "mods.json");
        if (File.Exists(json)) WriteAtomic(json, SetInModsJson(File.ReadAllText(json), name, enabled));

        // enabled.txt overrides the manifest — it must go when disabling.
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

    private static string SetInModsJson(string content, string name, bool enabled)
    {
        var list = new List<Dictionary<string, object>>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var n = el.TryGetProperty("mod_name", out var nn) ? nn.GetString() ?? "" : "";
                var en = el.TryGetProperty("mod_enabled", out var ee) && ee.ValueKind == JsonValueKind.True;
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) en = enabled;
                list.Add(new Dictionary<string, object> { ["mod_name"] = n, ["mod_enabled"] = en });
            }
        }
        catch { list.Clear(); }
        if (!list.Any(d => string.Equals((string)d["mod_name"], name, StringComparison.OrdinalIgnoreCase)))
            list.Add(new Dictionary<string, object> { ["mod_name"] = name, ["mod_enabled"] = enabled });
        return JsonSerializer.Serialize(list, Json);
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }
}
