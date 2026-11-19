using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>
/// The one rule for "is this FILE the game's own". A row with any such file is marked
/// <see cref="Mod.IsBase"/> by the scan, and nothing 626 does turns it off: bulk operations skip it,
/// an explicit toggle or uninstall refuses in words (<see cref="BaseGameFileException"/>). Turning one
/// back ON is always allowed, so a base file an older build moved to holding can come home.
///
/// <para><b>Bethesda (Creation Engine).</b> The game lists its own masters, Creation Club content and
/// archives in <c>Data</c> beside the mods, under the same extensions. Three signals, all by name:
/// the base masters every Creation Engine game ships (<see cref="IsBethesdaBaseMaster"/>), every file
/// the game's own Creation Club list in the game root names (<c>Skyrim.ccc</c>, <c>Fallout4.ccc</c>,
/// <c>Starfield.ccc</c>), and the <c>.bsa</c>/<c>.ba2</c> archives that belong to either
/// (<c>Skyrim - Textures0.bsa</c>, <c>ccBGSSSE001-Fish.bsa</c>).</para>
///
/// <para><b>Unreal.</b> A location that IS the game's <c>Content/Paks</c> (the paks-root form, or a
/// files-form location pointed at it) mixes base paks with mods. <see cref="IsBaseGameArchive"/> is the
/// one predicate, shared with Safe Clear. It is deliberately narrow: a broad <c>*-Windows*.pak</c> rule
/// would swallow real mods such as <c>BetterHUD-WindowsNoEditor.pak</c> (Safe Clear round 6).</para>
/// </summary>
public static class BaseGameFiles
{
    // Base masters across every Creation Engine game. Case-insensitive.
    private static readonly HashSet<string> BethesdaMasters = new(StringComparer.OrdinalIgnoreCase)
    {
        // Skyrim (LE / SE / AE / VR). _ResourcePack.esl (and its .bsa, by the archive rule) ships with AE 1.6.1130+.
        "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm", "SkyrimVR.esm",
        "_ResourcePack.esl",
        // Fallout 4 (and VR)
        "Fallout4.esm", "Fallout4_VR.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm", "DLCworkshop02.esm",
        "DLCworkshop03.esm", "DLCNukaWorld.esm", "DLCUltraHighResolution.esm",
        // Starfield (SFBGS*.esm is matched by pattern below)
        "Starfield.esm", "Constellation.esm", "OldMars.esm", "BlueprintShips-Starfield.esm", "ShatteredSpace.esm",
        // Oblivion, Fallout 3, Fallout: New Vegas
        "Oblivion.esm", "Fallout3.esm", "FalloutNV.esm",
        // Fallout 3 DLC
        "Anchorage.esm", "ThePitt.esm", "BrokenSteel.esm", "PointLookout.esm", "Zeta.esm",
        // Fallout: New Vegas DLC and the pre-order packs
        "DeadMoney.esm", "HonestHearts.esm", "OldWorldBlues.esm", "LonesomeRoad.esm", "GunRunnersArsenal.esm",
        "ClassicPack.esm", "MercenaryPack.esm", "TribalPack.esm", "CaravanPack.esm",
    };

    // Starfield's own update masters (SFBGS003.esm, SFBGS004.esm, ...).
    private static readonly Regex StarfieldUpdateMaster = new(@"^SFBGS[^.\\/]*\.esm$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Fallout 3 and New Vegas name their base archives "Fallout - Meshes.bsa", after no master at all. Only
    // honoured in a folder that holds one of those games' masters (FalloutArchivesApply).
    private static readonly string[] ArchiveOnlyStems = { "Fallout" };
    private static readonly string[] FalloutArchiveMasters = { "Fallout3.esm", "FalloutNV.esm" };

    // Archive owners that are not a plugin's stem: Skyrim VR's master is SkyrimVR.esm, its archives are
    // "Skyrim_VR - Main.bsa". Read by the archive rule exactly as a base plugin's stem is.
    private static readonly HashSet<string> ArchiveOwnerAliases = new(StringComparer.OrdinalIgnoreCase) { "Skyrim_VR" };

    // Oblivion's official DLC ships as .esp and Oblivion keeps no .ccc. Only the game's own beside Oblivion.esm
    // (OblivionDlcApplies), so a Skyrim mod that happens to be called Knights.esp stays a mod.
    private static readonly HashSet<string> OblivionDlcPlugins = new(StringComparer.OrdinalIgnoreCase)
    {
        "DLCShiveringIsles.esp", "Knights.esp", "DLCBattlehornCastle.esp", "DLCFrostcrag.esp", "DLCHorseArmor.esp",
        "DLCMehrunesRazor.esp", "DLCOrrery.esp", "DLCSpellTomes.esp", "DLCThievesDen.esp", "DLCVileLair.esp",
    };

    /// <summary>True when <paramref name="dataDir"/> holds Oblivion.esm, which is when Oblivion's DLC
    /// plugins (and their archives) are that game's own.</summary>
    public static bool OblivionDlcApplies(string? dataDir)
    {
        if (string.IsNullOrEmpty(dataDir)) return false;
        try { return File.Exists(Path.Combine(dataDir, "Oblivion.esm")); }
        catch { return false; }
    }

    /// <summary>True when <paramref name="dataDir"/> holds Fallout 3's or New Vegas's master, which is when
    /// "Fallout - *.bsa" archives are that game's own.</summary>
    public static bool FalloutArchivesApply(string? dataDir)
    {
        if (string.IsNullOrEmpty(dataDir)) return false;
        try { return FalloutArchiveMasters.Any(m => File.Exists(Path.Combine(dataDir, m))); }
        catch { return false; }
    }

    /// <summary>True for a Creation Engine plugin extension (.esm, .esp, .esl).</summary>
    public static bool IsPlugin(string? fileName)
        => PluginExts.Contains(Path.GetExtension(fileName ?? ""), StringComparer.OrdinalIgnoreCase);

    /// <summary>The Creation Club lists a Bethesda game keeps in its root: one filename per line.</summary>
    public static readonly IReadOnlyList<string> CreationClubLists = new[] { "Skyrim.ccc", "Fallout4.ccc", "Starfield.ccc" };

    private static readonly string[] PluginExts = { ".esm", ".esp", ".esl" };
    private static readonly string[] ArchiveExts = { ".bsa", ".ba2" };

    /// <summary>True for a base master every copy of a Creation Engine game ships. Name only.</summary>
    public static bool IsBethesdaBaseMaster(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileName(fileName);
        return BethesdaMasters.Contains(name) || StarfieldUpdateMaster.IsMatch(name);
    }

    /// <summary>
    /// Every filename the game's own Creation Club lists in <paramref name="gameRoot"/> name. Whichever
    /// lists exist are read; a missing or unreadable one counts as empty, so the static masters still apply.
    /// </summary>
    public static IReadOnlySet<string> ReadCreationClub(string? gameRoot)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(gameRoot)) return set;
        foreach (var list in CreationClubLists)
        {
            try
            {
                var path = Path.Combine(gameRoot, list);
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var name = line.Trim();
                    if (name.Length > 0 && name.IndexOfAny(new[] { '/', '\\' }) < 0) set.Add(name);
                }
            }
            catch { /* unreadable: no entries from this list */ }
        }
        return set;
    }

    /// <summary>
    /// True when a file in a Bethesda <c>Data</c> folder is the game's own: a base master, a file the
    /// Creation Club list names, or a <c>.bsa</c>/<c>.ba2</c> whose stem is a base plugin's stem or starts
    /// with <c>"&lt;base stem&gt; - "</c> (<c>"Fallout - "</c> too when <paramref name="falloutArchives"/>).
    /// <para>This is the FILE rule. A ROW is judged by <see cref="Judge.BaseFileOfRow"/>: a row that holds a
    /// plugin is the game's only when one of its plugins is, so <c>Dawnguard - Fixes.bsa</c> stays a mod's
    /// archive beside <c>Dawnguard - Fixes.esp</c>.</para>
    /// </summary>
    /// <param name="oblivionDlc">Count Oblivion's DLC plugins (true only beside Oblivion.esm).</param>
    public static bool IsBethesdaBaseFile(string? fileName, IReadOnlySet<string> creationClub, bool falloutArchives = false,
        bool oblivionDlc = false)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileName(fileName);
        var ext = Path.GetExtension(name);
        bool BasePlugin(string n) => IsBethesdaBaseMaster(n) || creationClub.Contains(n) || (oblivionDlc && OblivionDlcPlugins.Contains(n));
        if (PluginExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return BasePlugin(name);
        if (!ArchiveExts.Contains(ext, StringComparer.OrdinalIgnoreCase)) return false;
        if (creationClub.Contains(name)) return true;

        var stem = Path.GetFileNameWithoutExtension(name);
        // FIRST " - " on purpose: PluginArchives.OwnerStem names an orphan archive's row by the LAST one, and the
        // base check must stay on this split so a base-prefixed archive always locks.
        var dash = stem.IndexOf(" - ", StringComparison.Ordinal);
        var owner = dash > 0 ? stem[..dash] : stem;
        if (dash > 0 && falloutArchives && ArchiveOnlyStems.Contains(owner, StringComparer.OrdinalIgnoreCase)) return true;
        if (ArchiveOwnerAliases.Contains(owner)) return true;
        foreach (var pe in PluginExts)
            if (BasePlugin(owner + pe)) return true;
        return false;
    }

    /// <summary>
    /// A pak/ucas/utoc that is the base game's own: <see cref="PakClassifier.IsBaseGamePak"/> on its pak
    /// name, or a UE5 <c>global.ucas</c> / <c>global.utoc</c>. The one implementation, shared by the scan,
    /// the toggle guard and Safe Clear.
    /// </summary>
    public static bool IsBaseGameArchive(string? fileName, long size)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var name = Path.GetFileName(fileName);
        var ext = Path.GetExtension(name);
        if (!(ext.Equals(".pak", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ucas", StringComparison.OrdinalIgnoreCase)
              || ext.Equals(".utoc", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (Path.GetFileNameWithoutExtension(name).Equals("global", StringComparison.OrdinalIgnoreCase)) return true;
        return PakClassifier.IsBaseGamePak(Path.ChangeExtension(name, ".pak"), size);
    }

    /// <summary>True when a location is the game's own <c>Content/Paks</c> folder, where base paks and mods
    /// share one directory: the paks-root form, or a files-form location whose folder is <c>Content/Paks</c>.
    /// A dedicated mod folder (<c>~mods</c>, <c>LogicMods</c>) is not.</summary>
    public static bool IsSharedPaksFolder(ModLocationCtx loc)
    {
        if (loc.Form == "paks-root") return true;
        if (loc.Form != "files") return false;
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(loc.Abs));
            var paks = Path.GetFileName(full);
            var content = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
            return paks.Equals("Paks", StringComparison.OrdinalIgnoreCase)
                   && content.Equals("Content", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ---------- FromSoft direct-inject: a game file the catalog matches by NAME alone ----------

    // Catalog signatures (KnownDirectInjectMod) that match a file by its name only, where that name is ALSO
    // a file the game itself ships in the play folder. Elden Ring's Game\regulation.bin is the one: the
    // "Modded regulation.bin" entry cannot tell the game's file from a modded one by name.
    private static readonly HashSet<string> DirectInjectGameFileNames = new(StringComparer.OrdinalIgnoreCase) { "regulation.bin" };

    /// <summary>True when a direct-inject entry (a play-folder-relative name) is one the game itself ships,
    /// whatever else is true of it. Name only: it says the file CAN be the game's. Whether it is a mod is
    /// provenance (<see cref="IsDirectInjectGameFile"/>).</summary>
    public static bool IsDirectInjectGameFileName(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return false;
        var rel = entry.Replace('\\', '/').Trim('/');
        return rel.IndexOf('/') < 0 && DirectInjectGameFileNames.Contains(rel);
    }

    /// <summary>
    /// True when a direct-inject entry is the game's own file: it is a name the game ships
    /// (<see cref="IsDirectInjectGameFileName"/>) and 626 has no record of putting it there
    /// (<see cref="Placed"/>). A file is a mod only with provenance. A modded copy installed by hand, before
    /// 626 or by another tool, has none, and reads as the game's: locked on, never moved. That is the safe
    /// side of the call, because moving it leaves the game without the file either way.
    /// </summary>
    /// <param name="copyRoot">Where the copy being judged sits: null for the live file in the play folder, or a holding
    /// folder for a held copy. Provenance is about a copy, not a path: while a held 626 mod is off, the game's original can
    /// be live at the same path, and each must read as what it is.</param>
    public static bool IsDirectInjectGameFile(string? entry, Placed placed, string? copyRoot = null)
        => IsDirectInjectGameFileName(entry)
           && !placed.Claims(entry!, copyRoot is null ? null : Path.Combine(copyRoot, entry!));

    /// <summary>A direct-inject row is the game's when any file it owns is (<see cref="IsDirectInjectGameFile"/>).</summary>
    public static bool IsDirectInjectGameRow(IEnumerable<string> entries, Placed placed, string? copyRoot = null)
        => entries.Any(e => IsDirectInjectGameFile(e, placed, copyRoot));

    /// <summary>
    /// What 626 can prove it put in a play folder. Three records, each read only when asked (so a listing with
    /// nothing to judge costs no disk): an install record naming the file in THIS folder
    /// (<see cref="ModInstallRegistry"/>, matched by its location path so a same-named file in another folder
    /// proves nothing); the replaced store beside the game (<c>_626\replaced</c>, which a direct-inject drop
    /// writes the displaced original into, matched by play-folder-relative path); and the replaced store in the
    /// data folder, matched by the exact absolute path (its relative paths are relative to whichever mod
    /// location that intake used, so they prove nothing about the play folder).
    /// </summary>
    public sealed class Placed
    {
        private readonly string? _gameRoot;
        private readonly string? _dataDir;
        private readonly string? _folder;
        private HashSet<string>? _rels;
        // What each recorded replace says about content: the hash of the file 626 placed, where the stashed original is,
        // and when. Keyed by the replaced file's full path, which both stores record.
        private Dictionary<string, List<(string? PlacedSha, string StashedCopy, DateTime TakenUtc)>>? _byFull;
        private DateTime? _exeUtc;
        private IReadOnlyDictionary<string, IReadOnlySet<string>>? _swappedIn;
        private readonly Dictionary<string, string?> _hashes = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="gameRoot">The game root, which an install record's location path is relative to.</param>
        /// <param name="dataDir">Where install records and the data-folder replaced store live.</param>
        /// <param name="folder">The folder the entries are relative to (the play folder).</param>
        public Placed(string? gameRoot, string? dataDir, string? folder)
        {
            _gameRoot = gameRoot;
            _dataDir = dataDir;
            _folder = folder;
        }

        /// <summary>Nothing is proven: every name the game ships reads as the game's.</summary>
        public static Placed None => new(null, null, null);

        /// <summary>True when the live file at <paramref name="rel"/> (relative to the play folder) is 626's.</summary>
        public bool Claims(string rel) => Claims(rel, null);

        /// <summary>True when a copy of <paramref name="rel"/> is 626's: <paramref name="copyAbs"/> for a held copy, or the
        /// live file in the play folder when null.
        ///
        /// <para>A record that names the path is necessary, not sufficient. A replace record proves 626 once replaced the
        /// path, and the file there can since have been put back by Steam's verify or replaced by a patch: read as 626's,
        /// the game's own file listed as a mod and a return to vanilla moved it (#397 audit; the 2026-11-18 smoke of
        /// #418). So, by content: the file 626 placed is 626's; a stashed original, or one a held record swapped back in,
        /// is the game's; and when 626 recorded what it placed and this copy is neither, it is the game's (a patch's new
        /// version). An install record, or a replace from a build that recorded no hash, still claims by path when the
        /// content proves nothing either way, as before.</para></summary>
        public bool Claims(string rel, string? copyAbs)
        {
            if (string.IsNullOrEmpty(_folder) || string.IsNullOrWhiteSpace(rel)) return false;
            Load();
            var norm = NormRel(rel);
            var full = FullNorm(Path.Combine(_folder, rel));
            var replaces = full is not null && _byFull!.TryGetValue(full, out var r) ? r : null;
            // An install record alone claims by path. One that names a REPLACED file goes through the content check like
            // any replace, whichever store holds the replace (the Scanner lane writes both).
            var byInstall = _rels!.Contains(norm) && replaces is null;
            if (replaces is null && !byInstall) return false;
            var hashRecorded = replaces is not null && replaces.Any(x => x.PlacedSha is { Length: > 0 });

            var copy = copyAbs ?? Path.Combine(_folder, rel);
            // Unreadable: nothing to compare. With a recorded hash, 626 can't show the file is the one it placed, so it is
            // the game's (the safe side); with only a path record, the record stands, as before.
            if (Hash(copy) is not { } sha) return !hashRecorded;
            if (replaces is not null)
            {
                if (replaces.Any(x => string.Equals(x.PlacedSha, sha, StringComparison.OrdinalIgnoreCase))) return true;
                if (replaces.Any(x => string.Equals(Hash(x.StashedCopy), sha, StringComparison.OrdinalIgnoreCase))) return false;
            }
            if (_swappedIn!.TryGetValue(norm.ToLowerInvariant(), out var originals) && originals.Contains(sha)) return false;
            if (byInstall) return true;
            // 626 knows what it placed here and this is something else: the game's (verify, or a patch's new version).
            if (hashRecorded) return false;
            // A record from before placed hashes: matches nothing known, and the game executable was written after the
            // replace. That is a patch's file. The worst case is a 626 file reading as the game's and staying on, the
            // side the laws accept; the other way moves the game's file and the game won't start.
            if (replaces is not null && _exeUtc is { } exe && exe > replaces.Max(x => x.TakenUtc)) return false;
            return true;
        }

        private string? Hash(string path)
        {
            if (_hashes.TryGetValue(path, out var known)) return known;
            string? sha = null;
            try { if (File.Exists(path)) sha = RestorePoints.FileTally.Sha256(path); }
            catch { /* unreadable: compare nothing */ }
            return _hashes[path] = sha;
        }

        private void Load()
        {
            if (_rels is not null) return;
            var rels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var byFull = new Dictionary<string, List<(string?, string, DateTime)>>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, IReadOnlySet<string>> swappedIn = new Dictionary<string, IReadOnlySet<string>>();
            void Note(string? fullPath, ReplacedStore.ReplacedEntry e, string batchDir)
            {
                if (FullNorm(fullPath) is not { } p) return;
                if (!byFull.TryGetValue(p, out var list)) byFull[p] = list = new List<(string?, string, DateTime)>();
                list.Add((e.PlacedSha256, Path.Combine(batchDir, e.RelPath), e.TakenUtc));
            }
            try
            {
                if (!string.IsNullOrEmpty(_dataDir))
                {
                    var here = string.IsNullOrEmpty(_gameRoot) ? null : ModInstallRegistry.LocationPathFor(_gameRoot, _folder!);
                    if (here is not null)
                        foreach (var rec in ModInstallRegistry.List(_dataDir))
                            if (rec.LocationPath is { Length: > 0 } where && ModInstallRegistry.SameLocationPath(where, here))
                                foreach (var f in rec.Files) rels.Add(NormRel(f));

                    foreach (var (e, batch) in ReplacedStore.ReadAllWithBatch(Path.Combine(_dataDir, "replaced")))
                        Note(e.OriginalPath, e, batch);
                }
                foreach (var (e, batch) in ReplacedStore.ReadAllWithBatch(DirectInject.ReplacedRoot(_folder!)))
                {
                    if (!string.IsNullOrEmpty(e.RelPath)) rels.Add(NormRel(e.RelPath));
                    // Keyed by where the file sits in THIS play folder, whatever path the record was written with.
                    Note(Path.Combine(_folder!, e.RelPath), e, batch);
                }
            }
            catch { /* an unreadable record proves nothing; the file reads as the game's, the safe side */ }
            // Read apart from the stores, so a failure here never drops a replace record (the unsafe side).
            try { if (!string.IsNullOrEmpty(_dataDir)) swappedIn = DirectInject.SwappedInOriginalHashes(Path.Combine(_dataDir, "direct-disabled")); }
            catch { /* no swapped-in hashes known: the stash comparison still applies */ }
            try
            {
                var parent = Path.GetDirectoryName(_folder!);
                _exeUtc = StaleLoaders.GameExecutable(parent is null ? new[] { _folder! } : new[] { _folder!, parent })?.Utc;
            }
            catch { _exeUtc = null; }
            _rels = rels;
            _byFull = byFull;
            _swappedIn = swappedIn;
        }

        private static string NormRel(string p) => p.Replace('\\', '/').Trim('/');

        private static string? FullNorm(string? p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)); }
            catch { return null; }
        }
    }

    /// <summary>The refusal an explicit turn-off gets.</summary>
    public static string TurnOffRefusal(string file) => $"{file} is part of the game, so 626 won't turn it off.";

    /// <summary>The refusal an uninstall gets.</summary>
    public static string RemoveRefusal(string file) => $"{file} is part of the game, so 626 won't remove it.";

    /// <summary>
    /// The per-scan judge for one game: reads the game's Creation Club lists at most once, and answers
    /// per file. Create one per listing or per guarded operation.
    /// </summary>
    public sealed class Judge
    {
        private readonly string? _gameRoot;
        private readonly bool _bethesda;
        private IReadOnlySet<string>? _creationClub;

        public Judge(GameContext c) : this(c.Game.Engine, c.GameRoot) { }

        /// <param name="engine">The game's engine id; only <c>bethesda</c> turns on the Creation Engine rules.
        /// Null judges Unreal paks folders alone.</param>
        /// <param name="gameRoot">Where the game's Creation Club lists live.</param>
        public Judge(string? engine, string? gameRoot)
        {
            _gameRoot = gameRoot;
            _bethesda = string.Equals(engine, "bethesda", StringComparison.OrdinalIgnoreCase);
        }

        private IReadOnlySet<string> CreationClub => _creationClub ??= ReadCreationClub(_gameRoot);
        private readonly Dictionary<string, (bool Fallout, bool OblivionDlc)> _folderFacts = new(StringComparer.OrdinalIgnoreCase);

        // Which game's folder-scoped names apply here, read once per location.
        private (bool Fallout, bool OblivionDlc) FolderFacts(ModLocationCtx loc)
        {
            if (!_folderFacts.TryGetValue(loc.Abs, out var facts))
                _folderFacts[loc.Abs] = facts = (FalloutArchivesApply(loc.Abs), OblivionDlcApplies(loc.Abs));
            return facts;
        }

        /// <summary>
        /// The file a ROW is the game's by, or null when the row is a mod. On a Bethesda game a row that holds
        /// a plugin is judged by its plugins alone (the engine loads an archive for the plugin of the same
        /// stem, so beside a mod plugin the archive is the mod's), and the base plugin is what is named. A row
        /// of archives only is judged file by file (<c>Skyrim - Textures0.bsa</c>). <paramref name="size"/> is
        /// only asked for in an Unreal paks folder.
        /// </summary>
        public string? BaseFileOfRow(ModLocationCtx loc, IEnumerable<string> files, Func<string, long> size)
        {
            var list = files.ToList();
            var needsSize = NeedsSize(loc);
            if (_bethesda && list.Any(IsPlugin))
                return list.Where(IsPlugin).FirstOrDefault(f => IsBase(loc, f, 0)) is { } plugin ? Path.GetFileName(plugin) : null;
            return list.FirstOrDefault(f => IsBase(loc, f, needsSize ? size(f) : 0)) is { } file ? Path.GetFileName(file) : null;
        }

        /// <summary>True when this location can hold the game's own files beside mods at all. A false
        /// answer means no file there is ever base, so a caller can skip sizing.</summary>
        public bool Applies(ModLocationCtx loc) => _bethesda || IsSharedPaksFolder(loc);

        /// <summary>True when the location needs a file's size to judge it (an Unreal paks folder).</summary>
        public static bool NeedsSize(ModLocationCtx loc) => IsSharedPaksFolder(loc);

        /// <summary>Is <paramref name="file"/> (relative to <paramref name="loc"/>) the game's own?
        /// <paramref name="size"/> is only read for an Unreal paks folder.</summary>
        public bool IsBase(ModLocationCtx loc, string file, long size)
        {
            var name = Path.GetFileName(file);
            if (_bethesda)
            {
                var facts = FolderFacts(loc);
                if (IsBethesdaBaseFile(name, CreationClub, facts.Fallout, facts.OblivionDlc)) return true;
            }
            if (IsSharedPaksFolder(loc))
            {
                if (IsBaseGameArchive(name, size)) return true;
                // The paks-root guard has always refused any file past the size ceiling, whatever its extension.
                if (loc.Form == "paks-root" && PakClassifier.IsBaseGamePak(name, size)) return true;
            }
            return false;
        }
    }
}

/// <summary>
/// An explicit request to turn off or remove a file that is part of the game. The message is the
/// user-facing sentence, so callers show it as it is.
/// </summary>
public sealed class BaseGameFileException : InvalidOperationException
{
    public BaseGameFileException(string file, bool remove)
        : base(remove ? BaseGameFiles.RemoveRefusal(file) : BaseGameFiles.TurnOffRefusal(file))
    {
        File = file;
    }

    /// <summary>The game's file the request reached.</summary>
    public string File { get; }
}
