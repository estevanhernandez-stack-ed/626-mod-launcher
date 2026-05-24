using System.IO.Compression;
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
        var locations = game.ModLocations.Select((loc, idx) => new ModLocationCtx(
            string.IsNullOrEmpty(loc.Name) ? "loc" + idx : loc.Name,
            string.IsNullOrEmpty(loc.Label) ? (string.IsNullOrEmpty(loc.Name) ? "Location " + idx : loc.Name) : loc.Label,
            Path.IsPathRooted(loc.Path) ? loc.Path : Path.Combine(gameRoot, loc.Path),
            loc.Mirrors.Select(m => Path.IsPathRooted(m) ? m : Path.Combine(gameRoot, m)).ToList(),
            idx == 0)).ToList();
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
        if (c.GroupingRule == "by_folder") return BuildModListByFolder(c);
        var outMap = new Dictionary<string, Mod>();
        foreach (var loc in c.Locations)
        {
            foreach (var f in ListPakFiles(loc.Abs, c))
            {
                var k = ModKey(f, c);
                if (!outMap.TryGetValue(k, out var mod))
                {
                    mod = new Mod { Name = k, Location = loc.Name, Enabled = true, IsFolder = false };
                    outMap[k] = mod;
                }
                mod.Files.Add(f);
            }
        }
        foreach (var m in outMap.Values)
        {
            var loc = LocByName(m.Location, c);
            if (loc is null || loc.Mirrors.Count == 0) { m.OnServer = true; continue; }
            var mirrorFiles = new HashSet<string>();
            foreach (var mp in loc.Mirrors) foreach (var f in ListPakFiles(mp, c)) mirrorFiles.Add(f);
            m.OnServer = m.Files.Any(mirrorFiles.Contains);
        }
        if (c.ScanSubfolders == "warn")
        {
            foreach (var loc in c.Locations)
            {
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
            outMap[d.Name] = new Mod { Name = d.Name, Location = d.Location, Enabled = false, Files = d.Files.ToList(), IsFolder = false };
        }
        return outMap.Values.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<Mod> BuildModListByFolder(GameContext c)
    {
        var outMap = new Dictionary<string, Mod>();
        foreach (var loc in c.Locations)
            foreach (var f in ListSubfolders(loc.Abs))
                if (!outMap.ContainsKey(f))
                    outMap[f] = new Mod { Name = f, Location = loc.Name, Enabled = true, Files = new List<string> { f }, OnServer = false, IsFolder = true };
        foreach (var d in ListDisabled(c))
            if (!outMap.ContainsKey(d.Name))
                outMap[d.Name] = new Mod { Name = d.Name, Location = d.Location, Enabled = false, Files = d.Files.ToList(), IsFolder = true };
        return outMap.Values.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
    }

    private sealed record DisabledEntry(string Name, string Location, Dictionary<string, bool> HadOnServer, List<string> Files);

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
            try
            {
                var meta = JsonSerializer.Deserialize<DisabledMeta>(File.ReadAllText(Path.Combine(dir, "meta.json")), Json);
                if (meta is not null) { location = meta.Location ?? location; hadOnServer = meta.HadOnServer ?? hadOnServer; }
            }
            catch { /* keep defaults */ }
            var files = SafeReadFiles(dir).Where(n => n != "meta.json").ToList();
            result.Add(new DisabledEntry(name, location, hadOnServer, files));
        }
        return result;
    }

    // ---------- move helpers ----------

    private static void MoveAny(string src, string dest)
    {
        try
        {
            if (Directory.Exists(src)) Directory.Move(src, dest);
            else File.Move(src, dest);
        }
        catch
        {
            if (Directory.Exists(src)) { CopyDir(src, dest); DeleteDir(src); }
            else { File.Copy(src, dest); File.Delete(src); }
        }
    }

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

        // Phase 2: primary files are safely held — only now clear mirror copies, recording
        // which existed so enable can restore them.
        var hadOnServer = new Dictionary<string, bool>();
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
        var meta = new DisabledMeta { Location = m.Location, HadOnServer = hadOnServer, DisabledAt = DateTime.UtcNow.ToString("o"), IsFolder = m.IsFolder };
        File.WriteAllText(Path.Combine(dest, "meta.json"), JsonSerializer.Serialize(meta, Json));
    }

    private static void EnableMod(string name, GameContext c)
    {
        var src = Path.Combine(c.DisabledRoot, name);
        DisabledMeta? meta;
        try { meta = JsonSerializer.Deserialize<DisabledMeta>(File.ReadAllText(Path.Combine(src, "meta.json")), Json); }
        catch { return; }
        if (meta is null) return;
        var loc = LocByName(meta.Location, c);
        var hadOnServer = meta.HadOnServer ?? new Dictionary<string, bool>();
        Directory.CreateDirectory(loc.Abs);
        foreach (var mp in loc.Mirrors) Directory.CreateDirectory(mp);

        foreach (var entry in Directory.GetFileSystemEntries(src))
        {
            var entryName = Path.GetFileName(entry);
            if (entryName == "meta.json") continue;
            if (Directory.Exists(entry))
            {
                CopyDir(entry, Path.Combine(loc.Abs, entryName));
                if (hadOnServer.TryGetValue(entryName, out var v) && v)
                    foreach (var mp in loc.Mirrors) CopyDir(entry, Path.Combine(mp, entryName));
                DeleteDir(entry);
            }
            else
            {
                File.Copy(entry, Path.Combine(loc.Abs, entryName));
                if (!(hadOnServer.TryGetValue(entryName, out var v) && v == false))
                    foreach (var mp in loc.Mirrors) File.Copy(entry, Path.Combine(mp, entryName));
                File.Delete(entry);
            }
        }
        try { File.Delete(Path.Combine(src, "meta.json")); } catch { /* best effort */ }
        try { Directory.Delete(src); } catch { /* may be non-empty on partial */ }
    }

    private static void SetAllMods(bool enabled, GameContext c)
    {
        foreach (var m in BuildModList(c))
        {
            if (m.Enabled == enabled) continue;
            if (enabled) EnableMod(m.Name, c); else DisableEntry(m, c);
        }
    }

    private static void ApplyMode(string mode, GameContext c)
    {
        foreach (var m in ListWithClass(c))
        {
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
        var map = Classification.Seed(LoadClassification(c), mods.Select(m => (m.Name, m.OnServer)));
        try { SaveClassification(c, map); } catch { /* best effort */ }
        foreach (var m in mods)
        {
            m.Class = map.TryGetValue(m.Name, out var cl) ? cl : "both";
            var v = Variant.ParseVariant(m.Name);
            m.Base = v.Base;
            m.Variant = v.Tag;
        }
        return Metadata.MergeMetadata(mods, LoadMetadata(c));
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
        var result = new IntakeResult();
        foreach (var p in ExpandPaths(paths, c))
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
                    using var zip = System.IO.Compression.ZipFile.OpenRead(p);
                    foreach (var entry in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
                        if (Intake.ClassifyDrop(entry.FullName, c.Exts) != "mod") continue;
                        var name = Path.GetFileName(entry.FullName); // basename neutralizes zip-slip traversal
                        Directory.CreateDirectory(c.DataDir);
                        var tmp = Path.Combine(c.DataDir, "_tmp_" + name);
                        entry.ExtractToFile(tmp, overwrite: true);
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
        return result;
    }

    // ---------- metadata refresh (network client injected) ----------

    private static ModMeta MergeMeta(ModMeta cf, ModMeta? curated)
    {
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
}
