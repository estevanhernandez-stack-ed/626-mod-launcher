using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// Filesystem core: game-context resolution, mod scanning, reversible enable/disable,
/// profiles, MP/SP classification, and mod intake. No UI — pure System.IO, runs headless.
/// The public surface is async to match the shell; the IO itself is synchronous inside.
/// Mirrors scanner.js.
/// </summary>
public static class Scanner
{
    // Reads tolerate either casing; writes stay camelCase for Electron-app interop on shared files.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // The archive seam: one reader for all mod-archive reading (zip/7z/rar/tar via SharpCompress).
    // Static so the existing static intake methods keep their shape. Replaces raw ZipFile.OpenRead.
    private static readonly IArchiveReader Archive = new SharpCompressArchiveReader();

    // ---------- data dir + context (pure) ----------

    /// <summary>Where a game's launcher data lives (disabled mods, profiles, classification, metadata).</summary>
    public static string DataDirForGame(GameEntry game)
    {
        if (!string.IsNullOrEmpty(game.DataDir)) return game.DataDir;
        var gameRoot = string.IsNullOrEmpty(game.GameRoot) ? "." : game.GameRoot;
        var id = string.IsNullOrEmpty(game.Id) ? "game" : game.Id;
        var steam = Regex.Match(gameRoot, @"^(.*?)[\\/]steamapps[\\/]", RegexOptions.IgnoreCase);
        if (steam.Success) return Path.Combine(steam.Groups[1].Value, "_626mods", id);
        return Path.Combine(Path.GetDirectoryName(gameRoot) ?? ".", "_626mods", id);
    }

    public static GameContext GameContext(GameEntry? game)
    {
        game ??= new GameEntry();
        var gameRoot = Path.GetFullPath(string.IsNullOrEmpty(game.GameRoot) ? "." : game.GameRoot);
        var dataDir = DataDirForGame(game);
        var exts = (game.FileExtensions.Count > 0 ? game.FileExtensions : new[] { "pak" }).Select(e => e.ToLowerInvariant()).ToList();
        var fileRe = new Regex(@"\.(" + string.Join("|", exts) + ")$", RegexOptions.IgnoreCase);
        var defaultForm = game.GroupingRule == "by_folder" ? "folders" : "files";
        var locations = game.ModLocations.Select((loc, idx) => new ModLocationCtx(
            string.IsNullOrEmpty(loc.Name) ? "loc" + idx : loc.Name,
            string.IsNullOrEmpty(loc.Label) ? (string.IsNullOrEmpty(loc.Name) ? "Location " + idx : loc.Name) : loc.Label,
            Path.IsPathRooted(loc.Path) ? loc.Path : Path.Combine(gameRoot, loc.Path),
            loc.Mirrors.Select(m => Path.IsPathRooted(m) ? m : Path.Combine(gameRoot, m)).ToList(),
            idx == 0)
        {
            Form = string.IsNullOrEmpty(loc.Form) ? defaultForm : loc.Form,
            Managed = loc.Managed,
        }).ToList();

        // When the launcher owns a UE4SS install, its ue4ss\Mods folder is a real, launcher-managed
        // surface even though it isn't one of the game's configured modLocations — append it so Lua mods
        // there (installed + UE4SS's built-ins) get a row + toggle via the existing folders-form path.
        var installedFrameworks = Frameworks.FrameworkRegistry.List(dataDir);
        if (Ue4ssAutoLocation.ShouldAppend(installedFrameworks, locations.Select(l => l.Abs).ToList()))
            locations.Add(Ue4ssAutoLocation.For(installedFrameworks)!);
        return new GameContext
        {
            Game = game,
            GameRoot = gameRoot,
            DataDir = dataDir,
            DisabledRoot = Path.Combine(dataDir, "disabled"),
            ProfilesDir = Path.Combine(dataDir, "profiles"),
            SavesDir = Path.Combine(dataDir, "saves"),
            ClassificationPath = Path.Combine(dataDir, "classification.json"),
            MetadataPath = Path.Combine(dataDir, "metadata.json"),
            LoadOrderPath = Path.Combine(dataDir, "loadorder.json"),
            SaveDir = string.IsNullOrEmpty(game.SaveDir) ? null : game.SaveDir,
            Exts = exts,
            FileRe = fileRe,
            Locations = locations,
            GroupingRule = string.IsNullOrEmpty(game.GroupingRule) ? "filename_no_ext" : game.GroupingRule,
            ScanSubfolders = string.IsNullOrEmpty(game.ScanSubfolders) ? "warn" : game.ScanSubfolders,
            HasGame = !string.IsNullOrEmpty(game.Id),
        };
    }

    public static ModLocationCtx LocByName(string? name, GameContext c)
        => c.Locations.FirstOrDefault(l => l.Name == name) ?? c.Locations[0];

    // ---------- config cockpit ----------

    /// <summary>
    /// Write edited config back to a mod's config file: first copy the current file to a timestamped
    /// backup under our data dir (NEVER into the mod folder), then atomically replace the file.
    /// Editing a config VALUE is allowed even in a tool-owned folder (user-data); callers warn the user.
    /// </summary>
    public static Task WriteModConfigAsync(string configPath, string content, GameContext c)
    {
        if (File.Exists(configPath))
        {
            var backupDir = Path.Combine(c.DataDir, "config-backups",
                Path.GetFileName(Path.GetDirectoryName(configPath)) ?? "mod");
            Directory.CreateDirectory(backupDir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(configPath, Path.Combine(backupDir, $"{Path.GetFileName(configPath)}.{stamp}.bak"), overwrite: true);
        }
        var tmp = configPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, configPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
            throw;
        }
        return Task.CompletedTask;
    }

    // ---------- migration ----------

    public static Task<bool> MigrateDataDirAsync(GameContext c) => Task.FromResult(MigrateDataDir(c));

    private static bool MigrateDataDir(GameContext c)
    {
        var legacy = Path.Combine(c.GameRoot, "_mod_launcher_data");
        if (legacy == c.DataDir) return false;
        if (!Directory.Exists(legacy)) return false;
        if (Directory.Exists(c.DataDir)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(c.DataDir)!);
        MoveAny(legacy, c.DataDir);
        return true;
    }

    // ---------- scanning ----------

    private static string ModKey(string filename, GameContext c)
    {
        var baseName = c.FileRe.Replace(filename, "");
        baseName = LoadOrderApply.StripPrefix(baseName); // ignore a launcher load-order prefix (NNNN__)
        return c.GroupingRule switch
        {
            "strip_underscore_p_suffix" => Regex.Replace(baseName, "_[Pp]$", ""),
            "filename_first_segment" => baseName.Split('-', '_')[0],
            _ => baseName,
        };
    }

    private static IReadOnlyList<string> SafeReadFiles(string dir)
    {
        try { return Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<string> SafeReadDirs(string dir)
    {
        try { return Directory.GetDirectories(dir).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    private static IReadOnlyList<string> ListPakFiles(string dir, GameContext c)
        => SafeReadFiles(dir).Where(n => c.FileRe.IsMatch(n)).ToList();

    private static IReadOnlyList<string> ListSubfolders(string dir)
        => SafeReadDirs(dir).Where(n => !n.StartsWith('.') && !n.StartsWith('_')).ToList();

    public static Task<IReadOnlyList<Mod>> BuildModListAsync(GameContext c) => Task.FromResult(BuildModList(c));

    private static IReadOnlyList<Mod> BuildModList(GameContext c)
    {
        var outMap = new Dictionary<string, Mod>();
        // Each location is scanned according to its form: "folders" = one folder per mod (UE4SS Lua
        // mods); "files" = pak files grouped by filename. A managed location (Vortex) tags its mods.
        foreach (var loc in c.Locations)
        {
            // Runtime ownership decides the posture; the profile's Managed value is only a fallback.
            // A UE4SS folder with a manifest is a loader-driven location: it can Conduct when unowned.
            var owner = ToolOwnership.Detect(loc.Abs);
            var isUe4ss = loc.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(loc.Abs);
            var isBepInEx = loc.Form != "folders" && string.Equals(c.Game.Engine, "bepinex", StringComparison.OrdinalIgnoreCase);
            var posture = Coordination.PostureFor(owner, loc.Managed, loaderCanConduct: isUe4ss || isBepInEx);
            var readOnly = posture == Posture.Coexist;
            var managedLabel = owner?.ToString().ToLowerInvariant()
                ?? (readOnly ? loc.Managed : null);

            if (loc.Form == "folders")
            {
                foreach (var f in ListSubfolders(loc.Abs))
                {
                    if (outMap.ContainsKey(f)) continue;
                    // Reading the manifest is non-mutating, so we surface true state even in an
                    // owned folder. Loader-drive (writing) is granted only where Conductor (unowned).
                    var enabled = isUe4ss ? Ue4ssManifest.IsEnabled(loc.Abs, f) : true;
                    outMap[f] = new Mod
                    {
                        Name = f, Location = loc.Name, Enabled = enabled, Files = new List<string> { f },
                        OnServer = false, IsFolder = true, Managed = managedLabel, ReadOnly = readOnly,
                        Loader = isUe4ss ? "ue4ss" : null,
                        Builtin = isUe4ss && Ue4ssBuiltins.IsBuiltin(f),
                    };
                }
            }
            else if (isBepInEx)
            {
                foreach (var (name, file, enabled) in BepInExPlugins.Scan(loc.Abs))
                {
                    if (outMap.ContainsKey(name)) continue;
                    outMap[name] = new Mod
                    {
                        Name = name, Location = loc.Name, Enabled = enabled, IsFolder = false,
                        Files = new List<string> { file }, OnServer = false,
                        Managed = managedLabel, ReadOnly = readOnly, Loader = "bepinex",
                    };
                }
            }
            else
            {
                foreach (var f in ListPakFiles(loc.Abs, c))
                {
                    var k = ModKey(f, c);
                    if (!outMap.TryGetValue(k, out var mod))
                    {
                        mod = new Mod
                        {
                            Name = k, Location = loc.Name, Enabled = true, IsFolder = false,
                            Managed = managedLabel, ReadOnly = readOnly,
                        };
                        outMap[k] = mod;
                    }
                    mod.Files.Add(f);
                }
            }
        }
        // OnServer (multiplayer mirror presence) is only meaningful for pak-file locations.
        foreach (var m in outMap.Values)
        {
            if (m.IsFolder || m.Loader is not null) continue;
            var loc = LocByName(m.Location, c);
            if (loc is null || loc.Mirrors.Count == 0) { m.OnServer = true; continue; }
            var mirrorFiles = new HashSet<string>();
            foreach (var mp in loc.Mirrors) foreach (var f in ListPakFiles(mp, c)) mirrorFiles.Add(f);
            m.OnServer = m.Files.Any(mirrorFiles.Contains);
        }
        // The vortex-stash heuristic only applies to file locations — in a folder-form location the
        // subfolders ARE the mods, so matching them against mod names would flag every mod as itself.
        if (c.ScanSubfolders == "warn")
        {
            foreach (var loc in c.Locations)
            {
                if (loc.Form == "folders") continue;
                foreach (var sub in ListSubfolders(loc.Abs))
                {
                    var norm = Regex.Replace(Regex.Replace(sub, "^AAA-", ""), @"\s+", "").ToLowerInvariant();
                    foreach (var m in outMap.Values)
                    {
                        var needle = Regex.Replace(m.Name, "[_-]", "").ToLowerInvariant();
                        needle = needle.Length > 6 ? needle[..6] : needle;
                        if (needle.Length > 0 && norm.Contains(needle)) { m.HasVortexFolder = true; break; }
                    }
                }
            }
        }
        foreach (var d in ListDisabled(c))
        {
            if (outMap.ContainsKey(d.Name)) continue;
            outMap[d.Name] = new Mod
            {
                Name = d.Name, Location = d.Location, Enabled = false, Files = d.Files.ToList(),
                IsFolder = d.IsFolder, Managed = c.Locations.FirstOrDefault(l => l.Name == d.Location)?.Managed,
            };
        }
        return outMap.Values.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
    }

    private sealed record DisabledEntry(string Name, string Location, Dictionary<string, bool> HadOnServer, List<string> Files, bool IsFolder);

    private sealed class DisabledMeta
    {
        public string? Location { get; set; }
        public Dictionary<string, bool> HadOnServer { get; set; } = new();
        public string? DisabledAt { get; set; }
        public bool IsFolder { get; set; }
    }

    private static IReadOnlyList<DisabledEntry> ListDisabled(GameContext c)
    {
        var result = new List<DisabledEntry>();
        foreach (var name in SafeReadDirs(c.DisabledRoot))
        {
            var dir = Path.Combine(c.DisabledRoot, name);
            var location = c.Locations.Count > 0 ? c.Locations[0].Name : "";
            var hadOnServer = new Dictionary<string, bool>();
            var isFolder = false;
            try
            {
                var meta = JsonSerializer.Deserialize<DisabledMeta>(File.ReadAllText(Path.Combine(dir, "meta.json")), Json);
                if (meta is not null) { location = meta.Location ?? location; hadOnServer = meta.HadOnServer ?? hadOnServer; isFolder = meta.IsFolder; }
            }
            catch { /* keep defaults */ }
            var files = SafeReadFiles(dir).Where(n => n != "meta.json").ToList();
            result.Add(new DisabledEntry(name, location, hadOnServer, files, isFolder));
        }
        return result;
    }

    // ---------- move helpers ----------

    private static void MoveAny(string src, string dest) => SafeMove.Move(src, dest);

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    private static void DeleteDir(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // ---------- enable / disable ----------

    public static Task DisableModAsync(string name, GameContext c) { DisableMod(name, c); return Task.CompletedTask; }
    public static Task EnableModAsync(string name, GameContext c) { EnableMod(name, c); return Task.CompletedTask; }
    public static Task<EnableOutcome> EnableModWithOutcomeAsync(string name, GameContext c)
        => Task.FromResult(EnableMod(name, c));

    /// <summary>
    /// Explicit per-row toggle entry point. For UE4SS loader mods this flips the manifest
    /// (no content move) even when the folder is tool-owned — mirroring the config edit-with-warning
    /// exception. For non-loader mods it falls through to the normal gated path.
    /// IMPORTANT: bulk ops (SetAllMods, ApplyMode, LoadProfile) MUST NOT call this — they go
    /// through DisableEntry/EnableMod which keep their ReadOnly guard, so owned mods are skipped.
    /// </summary>
    public static Task SetLoaderModEnabledAsync(string name, bool enabled, GameContext c)
    {
        var m = BuildModList(c).FirstOrDefault(x => x.Name == name);
        if (m?.Loader == "ue4ss")
        {
            // Manifest flip only — never a content move. Allowed regardless of ReadOnly.
            try { Ue4ssManifest.SetEnabled(LocByName(m.Location, c).Abs, name, enabled); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't {(enabled ? "enable" : "disable")} \"{name}\" ({e.Message})", e); }
            return Task.CompletedTask;
        }
        if (m?.Loader == "bepinex")
        {
            try { BepInExPlugins.SetEnabled(LocByName(m.Location, c).Abs, name, enabled); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't {(enabled ? "enable" : "disable")} \"{name}\" ({e.Message})", e); }
            return Task.CompletedTask;
        }
        // Non-loader mod: fall back to the normal gated path (ReadOnly guard applies).
        if (enabled) EnableMod(name, c); else { var m2 = BuildModList(c).FirstOrDefault(x => x.Name == name); if (m2 is not null) DisableEntry(m2, c); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Permanently delete a mod — the one destructive op, gated by an explicit caller (the UI
    /// confirms). Removes live files from every location + mirror AND any disabled holding
    /// folder. Idempotent: unknown names are a no-op. Locked-file errors surface (game running).
    /// </summary>
    public static Task UninstallModAsync(string name, GameContext c) { UninstallMod(name, c); return Task.CompletedTask; }
    public static Task SetAllModsAsync(bool enabled, GameContext c) { SetAllMods(enabled, c); return Task.CompletedTask; }
    public static Task ApplyModeAsync(string mode, GameContext c) { ApplyMode(mode, c); return Task.CompletedTask; }

    private static void DisableMod(string name, GameContext c)
    {
        var m = BuildModList(c).FirstOrDefault(x => x.Name == name);
        if (m is null || !m.Enabled) return;
        DisableEntry(m, c);
    }

    private static void UninstallMod(string name, GameContext c)
    {
        var m = BuildModList(c).FirstOrDefault(x => x.Name == name);
        if (m is not null)
        {
            // Owned mods (Vortex/MO2-managed) must never be deleted by this launcher.
            // Uninstall is a single, explicit, destructive op — throw so the caller can surface
            // a clear message rather than silently succeeding or producing a false toast.
            if (m.ReadOnly)
                throw new InvalidOperationException($"\"{name}\" is managed by another tool — uninstall it there.");

            var loc = LocByName(m.Location, c);
            foreach (var f in m.Files)
            {
                DeletePath(Path.Combine(loc.Abs, f));
                foreach (var mp in loc.Mirrors) DeletePath(Path.Combine(mp, f));
            }
        }
        var held = Path.Combine(c.DisabledRoot, name);
        if (Directory.Exists(held)) DeleteDir(held);
    }

    private static void DeletePath(string p)
    {
        if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
        else if (File.Exists(p)) File.Delete(p);
    }

    private static void DisableEntry(Mod m, GameContext c)
    {
        // Owned mods are read-only — another tool manages their files. Skip, not error: this
        // is called from bulk loops (SetAllMods, ApplyMode, LoadProfile) where owned = expected.
        if (m.ReadOnly) return;
        // Loader-driven mods (e.g. UE4SS Conductor): flip the manifest, no file moves.
        if (m.Loader == "ue4ss")
        {
            try { Ue4ssManifest.SetEnabled(LocByName(m.Location, c).Abs, m.Name, enabled: false); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't disable \"{m.Name}\" ({e.Message})", e); }
            return;
        }
        if (m.Loader == "bepinex")
        {
            try { BepInExPlugins.SetEnabled(LocByName(m.Location, c).Abs, m.Name, enable: false); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't disable \"{m.Name}\" ({e.Message})", e); }
            return;
        }
        var loc = LocByName(m.Location, c);
        var dest = Path.Combine(c.DisabledRoot, m.Name);
        Directory.CreateDirectory(dest);
        var files = m.IsFolder ? new List<string> { m.Files[0] } : m.Files;

        // Phase 1: move every primary file into the holding folder. Any failure rolls back
        // the ones already moved so the mod is never left half-disabled, then surfaces it.
        var moved = new List<string>();
        try
        {
            foreach (var f in files)
            {
                MoveAny(Path.Combine(loc.Abs, f), Path.Combine(dest, f));
                moved.Add(f);
            }
        }
        catch (Exception e)
        {
            foreach (var f in moved) { try { MoveAny(Path.Combine(dest, f), Path.Combine(loc.Abs, f)); } catch { /* best effort */ } }
            try { DeleteDir(dest); } catch { /* best effort */ }
            throw new InvalidOperationException($"Couldn't disable \"{m.Name}\" ({e.Message})", e);
        }

        // Phase 2: primary files are safely held. Snapshot-first — write meta.json BEFORE clearing any
        // mirror, with hadOnServer provisionally true for every file. A crash mid-clear then errs toward
        // "had a mirror" (which enable safely recreates) rather than losing the record entirely. Rewrite
        // with confirmed values once the clear completes. Mirrors IniEditService.SaveWithBackup ordering.
        var metaPath = Path.Combine(dest, "meta.json");
        var disabledAt = DateTime.UtcNow.ToString("o");
        var hadOnServer = files.ToDictionary(f => f, _ => true);
        void WriteMeta() => AtomicJson.WriteJsonAtomic(metaPath,
            new DisabledMeta { Location = m.Location, HadOnServer = hadOnServer, DisabledAt = disabledAt, IsFolder = m.IsFolder });

        WriteMeta();   // provisional record exists before any mirror is touched

        foreach (var f in files)
        {
            var hadAny = false;
            foreach (var mp in loc.Mirrors)
            {
                var sPath = Path.Combine(mp, f);
                if (Directory.Exists(sPath)) { hadAny = true; DeleteDir(sPath); }
                else if (File.Exists(sPath)) { hadAny = true; File.Delete(sPath); }
            }
            hadOnServer[f] = hadAny;
        }

        WriteMeta();   // confirmed record
    }

    /// <summary>Result of an enable attempt — lets bulk / Safe-Clear callers see WHY a mod didn't
    /// re-enable instead of getting a silent no-op.</summary>
    public sealed record EnableOutcome(string Name, bool Enabled, bool Skipped, string? Reason);

    private static EnableOutcome EnableMod(string name, GameContext c)
    {
        // Loader-driven mods (e.g. UE4SS Conductor) are never moved to the disabled holding folder;
        // their enable state lives in the manifest. Key on m.Loader for symmetry with DisableEntry.
        var live = BuildModList(c).FirstOrDefault(x => x.Name == name);
        // Owned mods are content read-only; their manifest is flipped only via the explicit per-row
        // path (SetLoaderModEnabledAsync), never through a bulk/profile-reachable EnableMod call.
        // Mirrors DisableEntry, which guards ReadOnly first.
        if (live is { ReadOnly: true })
            return new EnableOutcome(name, false, true, "managed by another tool");
        if (live?.Loader == "ue4ss")
        {
            try { Ue4ssManifest.SetEnabled(LocByName(live.Location, c).Abs, name, enabled: true); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't enable \"{name}\" ({e.Message})", e); }
            return new EnableOutcome(name, true, false, null);
        }
        if (live?.Loader == "bepinex")
        {
            try { BepInExPlugins.SetEnabled(LocByName(live.Location, c).Abs, name, enable: true); }
            catch (Exception e) { throw new InvalidOperationException($"Couldn't enable \"{name}\" ({e.Message})", e); }
            return new EnableOutcome(name, true, false, null);
        }

        var src = Path.Combine(c.DisabledRoot, name);
        DisabledMeta? meta;
        try { meta = JsonSerializer.Deserialize<DisabledMeta>(File.ReadAllText(Path.Combine(src, "meta.json")), Json); }
        catch { return new EnableOutcome(name, false, true, "no readable disabled metadata"); }
        if (meta is null) return new EnableOutcome(name, false, true, "empty disabled metadata");
        var loc = LocByName(meta.Location, c);
        // If the target location is currently owned by another tool, leave it alone — the
        // meta.json records where this mod came from; restoring into an owned folder would
        // corrupt the external tool's deployment manifest.
        if (ToolOwnership.Detect(loc.Abs) is not null)
            return new EnableOutcome(name, false, true, "target folder now owned by another tool");
        var hadOnServer = meta.HadOnServer ?? new Dictionary<string, bool>();
        Directory.CreateDirectory(loc.Abs);
        foreach (var mp in loc.Mirrors) Directory.CreateDirectory(mp);

        // Copy every entry to live + the right mirrors. Pre-check each destination and throw a conflict
        // BEFORE writing (a collision is a real conflict, and it means we must never delete that path on
        // rollback). Track each destination in `created` BEFORE the write so a mid-copy failure (disk full,
        // nested error) rolls back the partial copy too — the pre-check guarantees we only ever created it.
        var created = new List<string>();
        try
        {
            foreach (var entry in Directory.GetFileSystemEntries(src))
            {
                var entryName = Path.GetFileName(entry);
                if (entryName == "meta.json") continue;
                var isDir = Directory.Exists(entry);

                // Live destination always; mirrors per the original hadOnServer rule:
                //   directory entry -> mirror only if hadOnServer[entry] == true
                //   file entry      -> mirror UNLESS hadOnServer[entry] == false
                var dests = new List<string> { Path.Combine(loc.Abs, entryName) };
                bool toMirrors = isDir
                    ? (hadOnServer.TryGetValue(entryName, out var vd) && vd)
                    : !(hadOnServer.TryGetValue(entryName, out var vf) && vf == false);
                if (toMirrors)
                    foreach (var mp in loc.Mirrors) dests.Add(Path.Combine(mp, entryName));

                foreach (var dst in dests)
                {
                    if (Directory.Exists(dst) || File.Exists(dst))
                        throw new IOException($"\"{entryName}\" already exists at \"{dst}\" — conflict.");
                    created.Add(dst);                                 // track BEFORE the write
                    if (isDir) CopyDir(entry, dst); else File.Copy(entry, dst);
                }
            }
        }
        catch (Exception e)
        {
            // Roll back only paths we created this run; the holding folder is left untouched.
            foreach (var p in created)
            {
                try { if (Directory.Exists(p)) Directory.Delete(p, recursive: true); else if (File.Exists(p)) File.Delete(p); }
                catch { /* best effort */ }
            }
            throw new InvalidOperationException($"Couldn't enable \"{name}\" ({e.Message})", e);
        }

        // All live/mirror copies succeeded — now tear down the holding folder.
        foreach (var entry in Directory.GetFileSystemEntries(src))
        {
            if (Path.GetFileName(entry) == "meta.json") continue;
            try { if (Directory.Exists(entry)) DeleteDir(entry); else File.Delete(entry); } catch { /* best effort */ }
        }
        try { File.Delete(Path.Combine(src, "meta.json")); } catch { /* best effort */ }
        try { Directory.Delete(src); } catch { /* may be non-empty on partial */ }
        return new EnableOutcome(name, true, false, null);
    }

    private static void SetAllMods(bool enabled, GameContext c)
    {
        foreach (var m in BuildModList(c))
        {
            if (m.ReadOnly) continue; // never mutate a folder another tool owns
            if (m.Enabled == enabled) continue;
            if (enabled) EnableMod(m.Name, c); else DisableEntry(m, c);
        }
    }

    private static void ApplyMode(string mode, GameContext c)
    {
        foreach (var m in ListWithClass(c))
        {
            if (m.ReadOnly) continue; // never mutate a folder another tool owns
            var want = Classification.ModeFilter(mode, m.Class ?? "both");
            if (m.Enabled && !want) DisableEntry(m, c);
            else if (!m.Enabled && want) EnableMod(m.Name, c);
        }
    }

    // ---------- classification + metadata storage ----------

    public static Dictionary<string, string> LoadClassification(GameContext c)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(c.ClassificationPath), Json) ?? new(); }
        catch { return new Dictionary<string, string>(); }
    }

    public static void SaveClassification(GameContext c, IReadOnlyDictionary<string, string> map)
    {
        Directory.CreateDirectory(c.DataDir);
        AtomicJson.WriteJsonAtomic(c.ClassificationPath, map);
    }

    public static Dictionary<string, ModMeta> LoadMetadata(GameContext c)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, ModMeta>>(File.ReadAllText(c.MetadataPath), Json) ?? new(); }
        catch { return new Dictionary<string, ModMeta>(); }
    }

    public static void SaveMetadata(GameContext c, IReadOnlyDictionary<string, ModMeta> meta)
    {
        Directory.CreateDirectory(c.DataDir);
        AtomicJson.WriteJsonAtomic(c.MetadataPath, meta);
    }

    public static Task<IReadOnlyList<Mod>> ListWithClassAsync(GameContext c) => Task.FromResult(ListWithClass(c));

    private static IReadOnlyList<Mod> ListWithClass(GameContext c)
    {
        var mods = BuildModList(c);
        var map = ClassifyInMemory(c, mods);
        try { SaveClassification(c, map); } catch { /* best effort */ }
        return Metadata.MergeMetadata(mods, LoadMetadata(c));
    }

    // Read-only: seed classification + set Class/Base/Variant in memory. No disk writes. Returns the seeded map.
    private static Dictionary<string, string> ClassifyInMemory(GameContext c, IReadOnlyList<Mod> mods)
    {
        var map = Classification.Seed(LoadClassification(c), mods.Select(m => (m.Name, m.OnServer)));
        foreach (var m in mods)
        {
            m.Class = map.TryGetValue(m.Name, out var cl) ? cl : "both";
            var v = Variant.ParseVariant(m.Name);
            m.Base = v.Base;
            m.Variant = v.Tag;
        }
        return map;
    }

    /// <summary>Read-only sibling of <see cref="ListWithClass"/>: scan + in-memory classify (Class/Base/Variant),
    /// no SaveClassification, no metadata merge. The shared mod-listing resolver uses this for the scanner world.</summary>
    public static IReadOnlyList<Mod> ListClassified(GameContext c)
    {
        var mods = BuildModList(c);
        ClassifyInMemory(c, mods);
        return mods;
    }

    /// <summary>Persist the auto-seeded classification exactly as <see cref="ListWithClass"/> does. Used by the
    /// App after the read-only resolver in scanner-world so per-reload persistence is byte-identical.</summary>
    public static void PersistClassification(GameContext c, IReadOnlyList<Mod> mods)
    {
        try { SaveClassification(c, Classification.Seed(LoadClassification(c), mods.Select(m => (m.Name, m.OnServer)))); }
        catch { /* best effort */ }
    }

    // ---------- load order ----------

    public static Task<IReadOnlyList<string>> GetLoadOrderAsync(GameContext c) => Task.FromResult(GetLoadOrder(c));
    public static Task ApplyLoadOrderAsync(GameContext c, IReadOnlyList<string> orderedKeys) { ApplyLoadOrder(c, orderedKeys); return Task.CompletedTask; }
    public static Task ResetLoadOrderAsync(GameContext c) { ResetLoadOrder(c); return Task.CompletedTask; }

    private static IReadOnlyList<string> LoadSavedOrder(GameContext c)
    {
        try { return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(c.LoadOrderPath), Json) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    private static void SaveLoadOrder(GameContext c, IReadOnlyList<string> keys)
    {
        Directory.CreateDirectory(c.DataDir);
        AtomicJson.WriteJsonAtomic(c.LoadOrderPath, keys);
    }

    /// <summary>The saved order reconciled with the currently-enabled mods (new append, missing drop).</summary>
    private static IReadOnlyList<string> GetLoadOrder(GameContext c)
    {
        var enabled = BuildModList(c).Where(m => m.Enabled).Select(m => m.Name);
        return LoadOrder.Reconcile(LoadSavedOrder(c), enabled);
    }

    /// <summary>
    /// Enforce load order for Unreal pak mods by prefixing each enabled mod's files with a
    /// zero-padded index. Purely additive (reversible via <see cref="ResetLoadOrder"/>); modKey
    /// ignores the prefix so identity/disable are unaffected. Persists the order.
    /// </summary>
    private static void ApplyLoadOrder(GameContext c, IReadOnlyList<string> orderedKeys)
    {
        var byKey = BuildModList(c).Where(m => m.Enabled && !m.ReadOnly && m.Loader is null).GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.First());
        var index = 0;
        foreach (var key in orderedKeys)
        {
            if (!byKey.TryGetValue(key, out var m)) continue;
            var loc = LocByName(m.Location, c);
            foreach (var f in m.Files)
            {
                var dest = LoadOrderApply.WithOrder(f, index);
                if (dest == f) continue;
                // Rename the primary and every server-build mirror identically so SP and MP keep
                // the same filename — desync here strands mirror copies (the Windrose bug).
                MoveIfFree(loc.Abs, f, dest);
                foreach (var mp in loc.Mirrors) MoveIfFree(mp, f, dest);
            }
            index++;
        }
        // UE4SS folder locations don't use pak prefixes — persist their relative order into the
        // loader manifest instead (only the mods that live in that folder, in the requested order).
        // Skip owned folders (Vortex/MO2): reordering an owned manifest is the same hands-off class
        // as the bulk/profile disable guard (ReadOnly). Bulk load-order is not the sanctioned per-row
        // warned path, so owned folder manifests stay untouched.
        foreach (var loc in c.Locations.Where(l => l.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(l.Abs)))
        {
            if (ToolOwnership.Detect(loc.Abs) is not null) continue;
            var locNames = new HashSet<string>(ListSubfolders(loc.Abs), StringComparer.OrdinalIgnoreCase);
            var orderedForLoc = orderedKeys.Where(locNames.Contains).ToList();
            if (orderedForLoc.Count > 0) Ue4ssManifest.SetOrder(loc.Abs, orderedForLoc);
        }

        SaveLoadOrder(c, orderedKeys);
    }

    /// <summary>Strip launcher load-order prefixes from every location (primary + mirrors), restoring original names.</summary>
    private static void ResetLoadOrder(GameContext c)
    {
        foreach (var loc in c.Locations)
        {
            // Never rename files inside a folder owned by another tool — even if a prefix
            // exists there (written externally), renaming it would corrupt the tool's manifest.
            if (ToolOwnership.Detect(loc.Abs) is not null) continue;
            StripPrefixesIn(loc.Abs, c);
            foreach (var mp in loc.Mirrors) StripPrefixesIn(mp, c);
        }
        try { File.Delete(c.LoadOrderPath); } catch { /* nothing to clear */ }
    }

    private static void StripPrefixesIn(string dir, GameContext c)
    {
        foreach (var f in ListPakFiles(dir, c))
        {
            var stripped = LoadOrderApply.StripPrefix(f);
            if (stripped != f) MoveIfFree(dir, f, stripped);
        }
    }

    /// <summary>Rename <paramref name="from"/> to <paramref name="to"/> within <paramref name="dir"/>,
    /// only when the source exists and the destination is free — idempotent, never clobbers.</summary>
    private static void MoveIfFree(string dir, string from, string to)
    {
        var src = Path.Combine(dir, from);
        var dst = Path.Combine(dir, to);
        if (File.Exists(src) && !File.Exists(dst)) File.Move(src, dst);
    }

    // ---------- profiles ----------

    private sealed record ProfileMod(string Name, bool Enabled);
    private sealed class ProfileData
    {
        public string? SavedAt { get; set; }
        public string? Game { get; set; }
        public List<ProfileMod> Mods { get; set; } = new();
    }

    public static Task<IReadOnlyList<string>> ListProfilesAsync(GameContext c) => Task.FromResult(ListProfiles(c));
    public static Task SaveProfileAsync(string name, GameContext c) { SaveProfile(name, c); return Task.CompletedTask; }
    public static Task LoadProfileAsync(string name, GameContext c) { LoadProfile(name, c); return Task.CompletedTask; }
    public static Task DeleteProfileAsync(string name, GameContext c) { DeleteProfile(name, c); return Task.CompletedTask; }

    private static IReadOnlyList<string> ListProfiles(GameContext c)
        => SafeReadFiles(c.ProfilesDir).Where(n => n.EndsWith(".json")).Select(n => n[..^".json".Length]).ToList();

    private static void SaveProfile(string name, GameContext c)
    {
        var safe = Profile.SafeProfileName(name);
        Directory.CreateDirectory(c.ProfilesDir);
        var snapshot = BuildModList(c).Select(m => new ProfileMod(m.Name, m.Enabled)).ToList();
        AtomicJson.WriteJsonAtomic(Path.Combine(c.ProfilesDir, safe + ".json"),
            new ProfileData { SavedAt = DateTime.UtcNow.ToString("o"), Game = c.Game.GameName, Mods = snapshot });
    }

    private static void LoadProfile(string name, GameContext c)
    {
        var safe = Profile.SafeProfileName(name);
        var data = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(Path.Combine(c.ProfilesDir, safe + ".json")), Json);
        if (data is null) return;
        var desired = data.Mods.ToDictionary(m => m.Name, m => m.Enabled);
        foreach (var m in BuildModList(c))
        {
            if (m.ReadOnly) continue; // never mutate a folder another tool owns (matches SetAllMods/ApplyMode)
            if (!desired.TryGetValue(m.Name, out var want)) continue;
            if (m.Enabled && !want) DisableMod(m.Name, c);
            else if (!m.Enabled && want) EnableMod(m.Name, c);
        }
    }

    private static void DeleteProfile(string name, GameContext c)
    {
        var safe = Profile.SafeProfileName(name);
        try { File.Delete(Path.Combine(c.ProfilesDir, safe + ".json")); } catch { /* best effort */ }
    }

    // ---------- intake ----------

    public static Task<IntakeResult> AddModsAsync(IEnumerable<string> paths, GameContext c) => Task.FromResult(AddMods(paths, c));

    /// <summary>The captured readme for a mod (cached at intake), or null. A derived cache under
    /// <c>dataDir\readmes\&lt;modKey&gt;.(md|txt)</c> — md preferred over txt.</summary>
    public static string? ReadmePathFor(string modKey, GameContext c)
    {
        if (!IsSafeKey(modKey)) return null;
        var dir = Path.Combine(c.DataDir, "readmes");
        foreach (var ext in new[] { ".md", ".txt" })
        {
            var p = Path.Combine(dir, modKey + ext);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // A modKey is filename-derived, but guard against separators / illegal chars so a crafted name
    // can never write the cache outside the readmes folder.
    private static bool IsSafeKey(string? key)
        => !string.IsNullOrWhiteSpace(key) && key!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Best-effort: cache each given zip's best readme under every mod key that zip
    /// contributes, so the viewer can surface it. Derived cache (re-captured on re-intake) — it
    /// never throws into the intake flow; a readme is never worth failing an install over.</summary>
    private static void CaptureReadmes(IEnumerable<string> zipPaths, GameContext c)
    {
        foreach (var zipPath in zipPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Intake.ArchiveExtensions.Any(a => zipPath.EndsWith(a, StringComparison.OrdinalIgnoreCase)) || !File.Exists(zipPath)) continue;
                using var zip = Archive.Open(zipPath);
                var names = zip.EntryNames;
                var pick = ReadmeCapture.PickReadme(names);
                if (pick is null) continue;
                var keys = names
                    .Where(n => Intake.ClassifyDrop(n, c.Exts) == "mod")
                    .Select(n => ModKey(Path.GetFileName(n), c))
                    .Where(IsSafeKey).Distinct().ToList();
                if (keys.Count == 0) continue;

                var ext = Path.GetExtension(pick).ToLowerInvariant();
                var dir = Path.Combine(c.DataDir, "readmes");
                Directory.CreateDirectory(dir);
                string? firstPath = null;
                foreach (var key in keys)
                {
                    var stem = Path.Combine(dir, key);
                    // A re-capture may switch ext (md<->txt); drop the stale sibling so resolution is unambiguous.
                    foreach (var other in new[] { ".md", ".txt" })
                        if (other != ext && File.Exists(stem + other)) { try { File.Delete(stem + other); } catch { /* best effort */ } }
                    var dest = stem + ext;
                    if (firstPath is null) { zip.Extract(pick, dest, overwrite: true); firstPath = dest; }
                    else File.Copy(firstPath, dest, overwrite: true);
                }
            }
            catch { /* derived cache — never break intake over a readme */ }
        }
    }

    private static bool PlaceFile(string srcAbs, string fileName, GameContext c)
    {
        var primary = c.Locations.FirstOrDefault() ?? throw new InvalidOperationException("No mod location configured for this game.");
        var dest = Path.Combine(primary.Abs, fileName);
        if (File.Exists(dest)) return false;
        Directory.CreateDirectory(primary.Abs);
        File.Copy(srcAbs, dest);
        foreach (var mp in primary.Mirrors)
        {
            Directory.CreateDirectory(mp);
            var mdest = Path.Combine(mp, fileName);
            if (!File.Exists(mdest)) File.Copy(srcAbs, mdest);
        }
        return true;
    }

    private static IEnumerable<string> WalkFiles(string dir)
    {
        foreach (var f in Directory.GetFiles(dir)) yield return f;
        foreach (var d in Directory.GetDirectories(dir))
            foreach (var f in WalkFiles(d)) yield return f;
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths, GameContext c)
    {
        var outList = new List<string>();
        foreach (var p in paths ?? Enumerable.Empty<string>())
        {
            if (Directory.Exists(p))
            {
                foreach (var f in WalkFiles(p)) if (Intake.ClassifyDrop(f, c.Exts) != "skip") outList.Add(f);
            }
            else outList.Add(p); // a file (or a missing path) passes through so its reason is reported
        }
        return outList;
    }

    private static IntakeResult AddMods(IEnumerable<string> paths, GameContext c)
    {
        var pathList = (paths ?? Enumerable.Empty<string>()).ToList();
        var result = new IntakeResult();

        // Guard: if the primary location is managed by another tool (Vortex/MO2), writing into
        // it would corrupt that tool's deployment manifest. Skip every dropped item with a clear
        // reason — do NOT write a single file into the owned folder.
        var primaryLoc = c.Locations.FirstOrDefault();
        if (primaryLoc is not null && ToolOwnership.Detect(primaryLoc.Abs) is not null)
        {
            foreach (var p in ExpandPaths(pathList, c))
                result.Skipped.Add(new SkippedItem(Path.GetFileName(p), "location is managed by another tool"));
            return result;
        }

        foreach (var p in ExpandPaths(pathList, c))
        {
            var kind = Intake.ClassifyDrop(p, c.Exts);
            try
            {
                if (kind == "mod")
                {
                    var name = Path.GetFileName(p);
                    if (PlaceFile(p, name, c)) result.Added.Add(name);
                    else result.Skipped.Add(new SkippedItem(name, "already installed"));
                }
                else if (kind == "zip")
                {
                    using var zip = Archive.Open(p);
                    foreach (var entryName in zip.EntryNames) // file entries only (dirs excluded by the seam)
                    {
                        if (Intake.ClassifyDrop(entryName, c.Exts) != "mod") continue;
                        var name = Path.GetFileName(entryName); // basename neutralizes zip-slip traversal
                        Directory.CreateDirectory(c.DataDir);
                        var tmp = Path.Combine(c.DataDir, "_tmp_" + name);
                        zip.Extract(entryName, tmp, overwrite: true);
                        try
                        {
                            if (PlaceFile(tmp, name, c)) result.Added.Add(name);
                            else result.Skipped.Add(new SkippedItem(name, "already installed"));
                        }
                        finally { try { File.Delete(tmp); } catch { /* best effort */ } }
                    }
                }
                else result.Skipped.Add(new SkippedItem(Path.GetFileName(p), "not a mod file"));
            }
            catch (Exception e) { result.Skipped.Add(new SkippedItem(Path.GetFileName(p), e.Message)); }
        }
        CaptureReadmes(pathList, c);
        return result;
    }

    /// <summary>The absolute destination a placed file would take (primary mod location), and its rel key.</summary>
    private static (string Abs, string Rel) DestFor(string fileName, GameContext c)
    {
        var primary = c.Locations.FirstOrDefault() ?? throw new InvalidOperationException("No mod location configured for this game.");
        return (Path.Combine(primary.Abs, fileName), fileName);
    }

    /// <summary>Classify a drop into add / collision / unsafe without writing anything.</summary>
    public static IntakePlan PlanIntake(IEnumerable<string> paths, GameContext c)
    {
        var add = new List<IntakeItem>();
        var collisions = new List<IntakeCollision>();
        var unsafeItems = new List<SkippedItem>();

        // Guard: if the primary location is owned by another tool (Vortex/MO2), mark every
        // incoming item as unsafe so the plan carries nothing to copy and the UI shows skips.
        var primaryLoc = c.Locations.FirstOrDefault();
        if (primaryLoc is not null && ToolOwnership.Detect(primaryLoc.Abs) is not null)
        {
            foreach (var p in ExpandPaths(paths, c))
                unsafeItems.Add(new SkippedItem(Path.GetFileName(p), "location is managed by another tool"));
            return new IntakePlan(add, collisions, unsafeItems);
        }

        foreach (var p in ExpandPaths(paths, c))
        {
            var kind = Intake.ClassifyDrop(p, c.Exts);
            if (kind == "skip") { unsafeItems.Add(new SkippedItem(Path.GetFileName(p), "not a mod file")); continue; }
            if (kind == "mod")
            {
                var name = Path.GetFileName(p);
                var (abs, rel) = DestFor(name, c);
                if (File.Exists(abs)) collisions.Add(new IntakeCollision(name, rel, abs, p));
                else add.Add(new IntakeItem(name, rel, p));
            }
            else if (kind == "zip")
            {
                using var zip = Archive.Open(p);
                foreach (var entryName in zip.EntryNames) // file entries only (dirs excluded by the seam)
                {
                    if (Intake.ClassifyDrop(entryName, c.Exts) != "mod") continue;
                    var name = Path.GetFileName(entryName);
                    var (abs, rel) = DestFor(name, c);
                    var incoming = $"{p}!{entryName}";
                    if (File.Exists(abs)) collisions.Add(new IntakeCollision(name, rel, abs, incoming));
                    else if (!add.Any(a => a.RelPath == rel)) add.Add(new IntakeItem(name, rel, incoming));
                }
            }
        }
        return new IntakePlan(add, collisions, unsafeItems);
    }

    /// <summary>Copy a planned source — a loose file path, or "zipPath!entryName" — to dest (overwrite allowed).</summary>
    private static void CopyPlanned(string incoming, string destAbs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destAbs)!);
        var bang = incoming.IndexOf('!');
        if (bang < 0) { File.Copy(incoming, destAbs, overwrite: true); return; }
        using var zip = Archive.Open(incoming[..bang]);
        zip.Extract(incoming[(bang + 1)..], destAbs, overwrite: true);
    }

    /// <summary>Execute a plan: install new files, back-up-then-replace chosen collisions, skip the rest.</summary>
    public static IntakeResult ExecuteIntake(IntakePlan plan, ISet<string> replaceRelPaths, GameContext c)
    {
        var result = new IntakeResult();
        foreach (var u in plan.Unsafe) result.Skipped.Add(u);

        var primary = c.Locations.FirstOrDefault() ?? throw new InvalidOperationException("No mod location configured for this game.");

        // Defensive guard: if the primary is owned, write nothing. PlanIntake already routes all
        // items into plan.Unsafe, so this only fires when ExecuteIntake is called directly with a
        // hand-constructed plan that bypasses PlanIntake.
        if (ToolOwnership.Detect(primary.Abs) is not null)
        {
            foreach (var item in plan.ToAdd)
                result.Skipped.Add(new SkippedItem(item.Name, "location is managed by another tool"));
            foreach (var col in plan.Collisions)
                result.Skipped.Add(new SkippedItem(col.Name, "location is managed by another tool"));
            return result;
        }

        Directory.CreateDirectory(primary.Abs);
        string? batch = null;
        string Batch() => batch ??= ReplacedStore.NewBatch(Path.Combine(c.DataDir, "replaced"));

        foreach (var item in plan.ToAdd)
        {
            try { CopyPlanned(item.IncomingSource, Path.Combine(primary.Abs, item.RelPath)); result.Added.Add(item.RelPath); }
            catch (Exception e) { result.Skipped.Add(new SkippedItem(item.Name, e.Message)); }
        }
        var manifest = new List<ReplacedStore.ReplacedEntry>();
        foreach (var col in plan.Collisions)
        {
            if (!replaceRelPaths.Contains(col.RelPath)) { result.Skipped.Add(new SkippedItem(col.Name, "kept existing")); continue; }
            string? backupPath = null;
            try
            {
                backupPath = ReplacedStore.Backup(col.ExistingPath, col.RelPath, Batch());
                CopyPlanned(col.IncomingSource, col.ExistingPath);
                manifest.Add(new ReplacedStore.ReplacedEntry(col.ExistingPath, col.RelPath, DateTime.UtcNow));
                result.Updated.Add(col.RelPath);
            }
            catch (Exception e)
            {
                // roll back the partial move so the original is never left missing
                try { if (backupPath != null && File.Exists(backupPath) && !File.Exists(col.ExistingPath)) File.Move(backupPath, col.ExistingPath); }
                catch { /* best effort */ }
                result.Skipped.Add(new SkippedItem(col.Name, e.Message));
            }
        }
        if (batch != null && manifest.Count > 0) ReplacedStore.WriteManifest(batch, manifest);

        // Capture readmes from every source zip the plan touched (added or collided) — keyed by the
        // mod keys those zips contribute, independent of which files were actually replaced.
        var zipSources = plan.ToAdd.Select(a => a.IncomingSource)
            .Concat(plan.Collisions.Select(col => col.IncomingSource))
            .Select(s => { var b = s.IndexOf('!'); return b < 0 ? null : s[..b]; })
            .Where(z => z is not null).Select(z => z!);
        CaptureReadmes(zipSources, c);
        return result;
    }

    // ---------- metadata refresh (network client injected) ----------

    private static ModMeta MergeMeta(ModMeta cf, ModMeta? curated)
    {
        // Manual entries lock the row. Auto-identify (Nexus md5 / CF fingerprint / name search) never
        // overrides what the user pasted via "Match to a mod…". Covers both parameter directions —
        // Scanner.cs has call sites with existing on either side.
        if (curated?.IsManual == true) return curated;
        if (cf.IsManual) return cf;

        if (curated is null) return cf;
        return new ModMeta
        {
            Title = curated.Title ?? cf.Title,
            Description = curated.Description ?? cf.Description,
            Author = curated.Author ?? cf.Author,
            AuthorUrl = curated.AuthorUrl ?? cf.AuthorUrl,
            Url = curated.Url ?? cf.Url,
            Source = curated.Source ?? cf.Source,
            Donate = curated.Donate ?? cf.Donate,
            Image = curated.Image ?? cf.Image,
            Downloads = curated.Downloads ?? cf.Downloads,
            CurseforgeId = curated.CurseforgeId ?? cf.CurseforgeId,
            Category = curated.Category ?? cf.Category,
            InstalledUtc = curated.InstalledUtc ?? cf.InstalledUtc,
            SourceConfidence = curated.SourceConfidence ?? cf.SourceConfidence,
        };
    }

    /// <summary>
    /// Search-by-name metadata refresh: clean each mod name into a query, search the game's
    /// CurseForge catalog, take a confident name match, merge into metadata.json (curated
    /// wins, CurseForge fills the gaps). The client is injected so this stays headless.
    /// </summary>
    public static async Task<RefreshResult> RefreshMetadataByNameAsync(GameContext c, ICurseForgeClient client, RefreshOptions? opts = null)
    {
        int? gameId = opts?.GameId ?? c.Game.CurseforgeGameId
            ?? (!string.IsNullOrEmpty(c.Game.GameName) ? await client.ResolveGameIdAsync(c.Game.GameName) : null);
        if (gameId is null) return new RefreshResult(0, 0, null);

        var mods = ListWithClass(c);
        var meta = LoadMetadata(c);
        var seen = new HashSet<string>();
        int matched = 0, total = 0;
        foreach (var m in mods)
        {
            var key = string.IsNullOrEmpty(m.Base) ? m.Name : m.Base;
            if (!seen.Add(key)) continue;
            total++;
            var query = NameMatch.CleanModName(key);
            IReadOnlyList<CfMod> hits;
            try { hits = await client.SearchAsync(gameId.Value, query); } catch { continue; }
            var best = NameMatch.PickBestMatch(query, hits, h => h.Name);
            if (best is null) continue;
            matched++;
            meta[key] = MergeMeta(CurseForgeRequests.MapMod(best), meta.GetValueOrDefault(key));
        }
        SaveMetadata(c, meta);
        return new RefreshResult(matched, total, gameId);
    }

    /// <summary>
    /// Exact identification of just-dropped files by CurseForge fingerprint: hash each file,
    /// ask CurseForge which mod it is, merge that metadata (curated wins). No fuzzy matching.
    /// </summary>
    public static async Task<IdentifyResult> FingerprintIdentifyAsync(GameContext c, ICurseForgeClient client, IEnumerable<string> fileNames)
    {
        var primary = c.Locations.FirstOrDefault();
        var names = fileNames?.ToList();
        if (primary is null || names is null || names.Count == 0) return new IdentifyResult(0);

        var fpToKey = new Dictionary<long, string>();
        var fps = new List<long>();
        foreach (var name in names)
        {
            byte[] buf;
            try { buf = File.ReadAllBytes(Path.Combine(primary.Abs, name)); } catch { continue; }
            long fp = Fingerprint.CurseForgeFingerprint(buf);
            fpToKey[fp] = Variant.ParseVariant(ModKey(name, c)).Base;
            fps.Add(fp);
        }
        if (fps.Count == 0) return new IdentifyResult(0);

        var matches = await client.GetFingerprintMatchesAsync(fps);
        var modIds = matches.Where(m => m.ModId is not null).Select(m => m.ModId!.Value).Distinct().ToList();
        if (modIds.Count == 0) return new IdentifyResult(0);

        var byId = new Dictionary<int, ModMeta>();
        foreach (var e in await client.GetModsAsync(modIds)) if (e.CurseforgeId is not null) byId[e.CurseforgeId.Value] = e;

        var meta = LoadMetadata(c);
        var matchedKeys = new HashSet<string>();
        foreach (var m in matches)
        {
            if (m.Fingerprint is null || !fpToKey.TryGetValue(m.Fingerprint.Value, out var key)) continue;
            if (m.ModId is null || !byId.TryGetValue(m.ModId.Value, out var e)) continue;
            meta[key] = MergeMeta(e, meta.GetValueOrDefault(key));
            matchedKeys.Add(key);
        }
        SaveMetadata(c, meta);
        return new IdentifyResult(matchedKeys.Count);
    }

    /// <summary>
    /// Exact identification of just-dropped files by Nexus md5: hash each file, ask Nexus which
    /// mod it is by md5_search, merge that metadata (curated/CF wins, Nexus fills the gaps). The
    /// md5 twin of <see cref="FingerprintIdentifyAsync"/>. No fuzzy matching. Best-effort per file.
    /// </summary>
    public static async Task<IdentifyResult> Md5IdentifyAsync(GameContext c, INexusClient nexus, IEnumerable<string> fileNames)
    {
        var domain = NexusDomains.Effective(c.Game);
        if (string.IsNullOrWhiteSpace(domain)) return new IdentifyResult(0); // no Nexus key for this game

        var primary = c.Locations.FirstOrDefault();
        var names = fileNames?.ToList();
        if (primary is null || names is null || names.Count == 0) return new IdentifyResult(0);

        var meta = LoadMetadata(c);
        var matchedKeys = new HashSet<string>();
        foreach (var name in names)
        {
            try
            {
                var md5 = Md5Hash.OfFile(Path.Combine(primary.Abs, name));
                var key = Variant.ParseVariant(ModKey(name, c)).Base;
                var match = await nexus.GetByMd5Async(domain, md5);
                if (match?.Meta is not null)
                {
                    meta[key] = MergeMeta(match.Meta, meta.GetValueOrDefault(key));
                    matchedKeys.Add(key);
                }
            }
            catch { /* one bad file doesn't abort the batch */ }
        }
        SaveMetadata(c, meta);
        return new IdentifyResult(matchedKeys.Count);
    }

    /// <summary>
    /// Identify just-dropped Nexus mods by the md5 of the **dropped archive** — Nexus indexes the
    /// published archive's hash, NOT the extracted file, so this is the correct intake path (an
    /// extracted .pak's md5 never matches). For each dropped .zip: hash it, ask Nexus, and apply the
    /// match to every mod key the zip contributes. Best-effort; needs a Nexus domain on the game.
    /// </summary>
    public static async Task<IdentifyResult> Md5IdentifyArchivesAsync(GameContext c, INexusClient nexus, IEnumerable<string> droppedPaths)
    {
        var domain = NexusDomains.Effective(c.Game);
        if (string.IsNullOrWhiteSpace(domain)) return new IdentifyResult(0);

        var meta = LoadMetadata(c);
        var matchedKeys = new HashSet<string>();
        foreach (var path in (droppedPaths ?? Enumerable.Empty<string>())
                     .Where(p => Intake.ArchiveExtensions.Any(a => p.EndsWith(a, StringComparison.OrdinalIgnoreCase))))
        {
            try
            {
                if (!File.Exists(path)) continue;
                var match = await nexus.GetByMd5Async(domain, Md5Hash.OfFile(path)); // hash the ARCHIVE
                if (match?.Meta is null) continue;

                // Get the mod keys this archive INSTALLS. Extension-based engines (pak/dll/jar) name mods
                // after their files, so ZipModKeys (filter by c.Exts + strip variants) is right. Catalog-based
                // engines (fromsoft direct-inject — c.Game.FileExtensions empty in the registry entry) name
                // mods from DirectInject.Catalog, so fall back to the signature matcher against the archive's
                // entries. Note: c.Exts is always non-empty (GameContext normalizes empty→["pak"]), so branch
                // on the raw registry entry instead.
                IReadOnlyList<string> keys;
                if (c.Game.FileExtensions.Count > 0)
                {
                    keys = ZipModKeys(path, c);
                }
                else
                {
                    using var zipForKeys = Archive.Open(path);
                    keys = DirectInject.MatchSignaturesInZip(zipForKeys.EntryNames);
                }

                foreach (var key in keys)
                {
                    // A Nexus archive-md5 match is exact provenance (this file IS the Nexus upload),
                    // so it is AUTHORITATIVE: Nexus identity (title/author/url/image) wins over any
                    // existing CurseForge match; CF only fills the fields Nexus lacks (downloads,
                    // source-code link). This is what makes backfill override a CF-won collision.
                    // Manual matches (ModMeta.IsManual) still lock the row — MergeMeta short-circuits.
                    meta[key] = MergeMeta(meta.GetValueOrDefault(key) ?? new ModMeta(), match.Meta);
                    matchedKeys.Add(key);
                }
            }
            catch { /* a miss / outage / unreadable zip never breaks intake */ }
        }
        if (matchedKeys.Count > 0) SaveMetadata(c, meta);
        return new IdentifyResult(matchedKeys.Count);
    }

    /// <summary>
    /// Identify already-installed Vortex-deployed mods by the Nexus modId recorded in Vortex's
    /// deployment manifest, fetching metadata from Nexus by id (no archive needed). Merges Nexus as
    /// authoritative provenance (same as the archive-md5 path); curated/CF fills what Nexus lacks.
    /// </summary>
    public static async Task<IdentifyResult> IdentifyVortexNexusAsync(GameContext c, INexusClient client)
    {
        var domain = NexusDomains.Effective(c.Game);
        if (string.IsNullOrEmpty(domain)) return new IdentifyResult(0);

        var meta = LoadMetadata(c);
        var matched = 0;
        foreach (var loc in c.Locations)
        {
            foreach (var r in VortexManifest.Read(loc.Abs))
            {
                if (r.NexusModId is not int id) continue;
                ModMeta? hit;
                try { hit = await client.GetModAsync(domain!, id); } catch { continue; }
                if (hit is null) continue;
                var key = Variant.ParseVariant(r.Folder).Base;
                meta[key] = MergeMeta(meta.GetValueOrDefault(key) ?? new ModMeta(), hit); // Nexus wins, existing fills
                matched++;
            }
        }
        if (matched > 0) SaveMetadata(c, meta);
        return new IdentifyResult(matched);
    }

    /// <summary>Resolve a CurseForge mod-page slug to a ModMeta. Uses the existing Search endpoint
    /// with the slug as the query; since CfMod carries no Slug field, takes the top result (CF search
    /// with the slug literal as the query is the best available resolution). Returns null on no-match / error.</summary>
    public static async Task<ModMeta?> LookupCurseForgeSlugAsync(ICurseForgeClient client, int gameId, string modSlug)
    {
        try
        {
            var hits = await client.SearchAsync(gameId, modSlug);
            var best = hits?.FirstOrDefault();
            return best is null ? null : CurseForgeRequests.MapMod(best);
        }
        catch { return null; }
    }

    /// <summary>Write a single entry into the per-game metadata.json, leaving everything else
    /// untouched. Used by the manual-match flow. (LoadMetadata + SaveMetadata are already atomic.)</summary>
    public static void WriteOneMeta(GameContext c, string modKey, ModMeta meta)
    {
        var existing = LoadMetadata(c);
        var next = new Dictionary<string, ModMeta>(existing, StringComparer.OrdinalIgnoreCase) { [modKey] = meta };
        SaveMetadata(c, next);
    }

    // The distinct mod keys an archive contributes (its mod-classified entries -> base keys).
    private static IReadOnlyList<string> ZipModKeys(string zipPath, GameContext c)
    {
        using var zip = Archive.Open(zipPath);
        return zip.EntryNames // file entries only (dirs excluded by the seam)
            .Where(n => Intake.ClassifyDrop(n, c.Exts) == "mod")
            .Select(n => Variant.ParseVariant(ModKey(Path.GetFileName(n), c)).Base)
            .Distinct()
            .ToList();
    }
}
