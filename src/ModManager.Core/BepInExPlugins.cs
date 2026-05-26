namespace ModManager.Core;

/// <summary>
/// BepInEx (Unity) plugin loader support. BepInEx loads every *.dll under BepInEx/plugins; a plugin
/// is disabled by renaming it to *.dll.disabled (BepInEx ignores non-.dll). We read true state and
/// flip it by rename — reversible, never a move-to-holding or delete. Pure System.IO.
/// </summary>
public static class BepInExPlugins
{
    private const string Dll = ".dll";
    private const string Disabled = ".dll.disabled";

    /// <summary>Enabled (*.dll) + disabled (*.dll.disabled) plugins, keyed by base name; enabled wins on dup.</summary>
    public static IReadOnlyList<(string Name, string File, bool Enabled)> Scan(string pluginsDir)
    {
        var byName = new Dictionary<string, (string File, bool Enabled)>(StringComparer.OrdinalIgnoreCase);
        string[] files;
        try { files = Directory.GetFiles(pluginsDir); } catch { return Array.Empty<(string, string, bool)>(); }
        foreach (var path in files)
        {
            var file = Path.GetFileName(path);
            string name; bool enabled;
            if (file.EndsWith(Disabled, StringComparison.OrdinalIgnoreCase)) { name = file[..^Disabled.Length]; enabled = false; }
            else if (file.EndsWith(Dll, StringComparison.OrdinalIgnoreCase)) { name = file[..^Dll.Length]; enabled = true; }
            else continue;
            if (name.Length == 0) continue;
            // Enabled form wins if both exist.
            if (!byName.TryGetValue(name, out var cur) || (enabled && !cur.Enabled))
                byName[name] = (file, enabled);
        }
        return byName.Select(kv => (kv.Key, kv.Value.File, kv.Value.Enabled)).ToList();
    }

    public static bool IsEnabled(string pluginsDir, string name)
        => File.Exists(Path.Combine(pluginsDir, name + Dll));

    /// <summary>Flip a plugin's enabled state by rename. Idempotent; no-op if the source isn't there.</summary>
    public static void SetEnabled(string pluginsDir, string name, bool enable)
    {
        var dll = Path.Combine(pluginsDir, name + Dll);
        var off = Path.Combine(pluginsDir, name + Disabled);
        if (enable)
        {
            if (!File.Exists(dll) && File.Exists(off)) File.Move(off, dll);
        }
        else
        {
            if (File.Exists(dll))
            {
                if (File.Exists(off)) throw new IOException($"\"{name}\" already has a disabled copy — resolve it first.");
                File.Move(dll, off);
            }
        }
    }
}
