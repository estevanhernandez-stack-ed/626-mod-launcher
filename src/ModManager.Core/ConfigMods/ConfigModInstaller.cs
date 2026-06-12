namespace ModManager.Core.ConfigMods;

/// <summary>
/// Installs UE config-tweak mods into a game's Saved/Config platform dir. Validate-then-write:
/// nothing touches the config tree until the whole payload is validated. Snapshot-first: every
/// existing target file is backed up (exact recorded path, .config-history layout modeled on
/// IniEditService) BEFORE the merged content is written. Registered in ConfigModStore so the row,
/// toggle, and uninstall are driven from one record. The only delete this class ever performs is
/// a file the mod itself created (ExistedAtInstall: false) — everything else is snapshot-restore.
/// </summary>
public static class ConfigModInstaller
{
    /// <summary>Stable id from the display name: lowercase, non-alnum -> '-', collapsed.</summary>
    public static string IdFor(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length > 0 ? slug : "config-mod";
    }

    public static ConfigModEntry Install(
        IReadOnlyList<(string Name, string Content)> payload, string configDir, string dataDir, string modName)
    {
        // ---- Validate (no writes yet) ----
        if (payload.Count == 0)
            throw new InvalidOperationException("Nothing to apply — the archive has no config settings.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in payload)
        {
            var baseName = Path.GetFileName(name.Replace('\\', '/'));
            if (name.Contains("..") || Path.IsPathRooted(name))
                throw new InvalidOperationException($"\"{name}\" escapes the config folder — refusing. Nothing was changed.");
            if (!ConfigMod.IsConfigFile(baseName))
                throw new InvalidOperationException($"\"{baseName}\" isn't a known UE config file — refusing. Nothing was changed.");
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException($"\"{baseName}\" is empty — nothing to apply.");
            if (!seen.Add(baseName))
                throw new InvalidOperationException($"The archive contains \"{baseName}\" twice — refusing. Nothing was changed.");
        }
        if (!Directory.Exists(configDir))
            throw new InvalidOperationException(
                "The game's config folder doesn't exist yet — run the game once so it creates its config, then drop the mod again.");

        var id = IdFor(modName);

        // Re-drop of the same mod: restore its previous state first so merges never stack.
        var prior = ConfigModStore.Load(dataDir).FirstOrDefault(e => e.Id == id);
        if (prior is not null) RestoreFiles(prior, configDir);

        // Truth-before-write: if the merge below crashes, the registry must say disabled (disk is
        // at the clean pre-mod state after the restore above), not claim the mod is still applied.
        // The success path's final Upsert overwrites this with the fresh enabled entry.
        if (prior is not null) ConfigModStore.Upsert(dataDir, prior with { Enabled = false });

        // ---- Snapshot + merge + write, per file ----
        var records = new List<ConfigFileRecord>();
        var written = new List<(string Target, ConfigFileRecord Rec)>();
        try
        {
            foreach (var (name, content) in payload)
            {
                var baseName = Path.GetFileName(name.Replace('\\', '/'));
                var target = Path.Combine(configDir, baseName);
                var existed = File.Exists(target);
                string? snapshot = null;
                if (existed)
                {
                    snapshot = SnapshotPathFor(dataDir, id, baseName);
                    Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
                    File.Copy(target, snapshot, overwrite: true); // snapshot BEFORE any write
                }
                var merged = existed ? ConfigMerge.Merge(File.ReadAllText(target), content) : content;
                WriteAtomic(target, merged);
                var rec = new ConfigFileRecord(baseName, existed, snapshot, content);
                records.Add(rec);
                written.Add((target, rec));
            }
        }
        catch
        {
            // Roll back whatever was written so the config tree is never left mid-state.
            foreach (var (target, rec) in written) RestoreOne(target, rec);
            throw;
        }

        var entry = new ConfigModEntry(id, modName, records, DateTime.UtcNow, Enabled: true);
        ConfigModStore.Upsert(dataDir, entry);
        return entry;
    }

    internal static string SnapshotPathFor(string dataDir, string id, string baseName)
        => Path.Combine(dataDir, ".config-history", id, $"{baseName}.{DateTime.UtcNow.Ticks}.bak");

    internal static void RestoreFiles(ConfigModEntry entry, string configDir)
    {
        foreach (var f in entry.Files)
            RestoreOne(Path.Combine(configDir, f.FileName), f);
    }

    private static void RestoreOne(string target, ConfigFileRecord rec)
    {
        if (rec.ExistedAtInstall && rec.SnapshotPath is not null && File.Exists(rec.SnapshotPath))
            File.Copy(rec.SnapshotPath, target, overwrite: true);
        else if (!rec.ExistedAtInstall && File.Exists(target))
            File.Delete(target); // the ONLY delete: a file the mod itself created
    }

    private static void WriteAtomic(string path, string content)
    {
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("n");
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }
}
