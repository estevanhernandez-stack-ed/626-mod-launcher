using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace ModManager.Core;

/// <summary>One save snapshot: its zip path, label, when it was taken, size, and whether the app made it (auto).</summary>
public sealed record SaveSnapshot(string Path, string FileName, string Label, DateTime TakenUtc, long SizeBytes, bool IsAuto);

/// <summary>A recognized save file: its name, extension, and human label (Vanilla / Seamless Co-op).</summary>
public sealed record SaveFile(string Name, string Extension, string TypeLabel);

/// <summary>
/// Built-in game-save snapshots. Backup zips the save folder to a timestamped archive kept
/// outside the save folder; restore swaps the save contents back, snapshotting the current
/// state first so a restore is itself undoable (operating law #3). Mirrors the launcher's
/// reversible-by-default stance. Pure System.IO — tested headless against temp dirs.
/// </summary>
public static partial class SaveManager
{
    private const string TimeFormat = "yyyyMMdd-HHmmss";

    /// <summary>Recognized save files, labeled by the game's declared save types (profile-driven).</summary>
    public static IReadOnlyList<SaveFile> ListSaveFiles(string saveDir, IReadOnlyList<SaveType> saveTypes)
    {
        if (!Directory.Exists(saveDir)) return Array.Empty<SaveFile>();
        var byExt = saveTypes.ToDictionary(t => t.Extension, t => t.Label, StringComparer.OrdinalIgnoreCase);
        return Directory.GetFiles(saveDir)
            .Where(f => byExt.ContainsKey(System.IO.Path.GetExtension(f)))
            .Select(f => new SaveFile(System.IO.Path.GetFileName(f), System.IO.Path.GetExtension(f), byExt[System.IO.Path.GetExtension(f)]))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Working on ONE world. See docs/superpowers/specs/2026-08-19-world-level-saves-design.md.
    //
    // Backup() is ZipFile.CreateFromDirectory and will happily zip a single world today - which is
    // precisely the danger. A world zip sitting in the whole-folder snapshot list is indistinguishable
    // from a whole-folder zip, and restoring it through Restore() would delete every OTHER world and
    // extract that one world's files loose at the top level. Silent, total, and recoverable only via
    // before-restore.
    //
    // So the two kinds are separated by LOCATION rather than by a naming convention somebody has to
    // remember. ListSnapshots reads exactly one directory and never recurses, so it cannot see these -
    // the separation is enforced by code that already exists.
    // ---------------------------------------------------------------------------------------------

    private const string WorldsSubdir = "worlds";

    /// <summary>Where one world's snapshots live: <c>&lt;snapshotsDir&gt;/worlds/&lt;worldId&gt;</c>.</summary>
    public static string WorldSnapshotsDir(string snapshotsDir, string worldId)
        => Path.Combine(snapshotsDir, WorldsSubdir, worldId);

    /// <summary>Snapshot a single world. Same naming and auto-prefix rules as the whole-folder
    /// backup, in that world's own directory.</summary>
    public static SaveSnapshot BackupWorld(string saveDir, string worldId, string snapshotsDir,
                                           string? label = null, bool auto = false)
    {
        var worldDir = Path.Combine(saveDir, worldId);
        if (!Directory.Exists(worldDir))
            throw new DirectoryNotFoundException($"World not found: {worldId}");
        return Backup(worldDir, WorldSnapshotsDir(snapshotsDir, worldId), label, auto);
    }

    /// <summary>The game's own backup history. A duplicate does not inherit twelve snapshots of
    /// somebody else's past — it would multiply the copy's size for nothing.</summary>
    private const string GameBackupDir = "backup";

    /// <summary>
    /// Copy a world into a fresh folder, optionally under a new name.
    ///
    /// <para><b>The rename is what makes this worth having.</b> Palworld reads a world's display name
    /// from inside the save, not from the folder, so a plain folder copy produces two worlds with the
    /// same name and no way to tell them apart in the game's own list — tested on a real install, and
    /// the reason this feature was dropped once already. Patching the copy's name answers exactly that
    /// objection. See <see cref="PalworldWorldName"/> for why the new name has a byte budget.</para>
    ///
    /// <para>Destroys nothing, so it needs no confirm and no snapshot. It does need the game closed:
    /// Palworld holds a loaded world in memory and flushes it on exit, so folder changes made while it
    /// runs are silently undone. That gate is the caller's — Core cannot see processes.</para>
    /// </summary>
    /// <returns>The new world's folder id.</returns>
    public static string DuplicateWorld(string saveDir, string worldId, string? newName = null)
    {
        var src = Path.Combine(saveDir, worldId);
        if (!Directory.Exists(src))
            throw new DirectoryNotFoundException($"World not found: {worldId}");

        var id = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var dst = Path.Combine(saveDir, id);
        if (Directory.Exists(dst))
            throw new InvalidOperationException($"A world folder called {id} already exists.");

        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src))
        {
            if (string.Equals(Path.GetFileName(dir), GameBackupDir, StringComparison.OrdinalIgnoreCase)) continue;
            CopyTree(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));

        // Only when there is a name to change. A joined world has no LevelMeta.sav, and asking for a
        // rename there is not an error - there is simply nothing for Palworld to show.
        if (!string.IsNullOrWhiteSpace(newName) && PalworldWorldName.Read(dst) is not null)
            PalworldWorldName.Write(dst, newName!);

        return id;
    }

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
    }

    /// <summary>That world's snapshots, newest first. Empty when it has never been backed up.</summary>
    public static IReadOnlyList<SaveSnapshot> ListWorldSnapshots(string snapshotsDir, string worldId)
        => ListSnapshots(WorldSnapshotsDir(snapshotsDir, worldId));

    /// <summary>
    /// Put one world back, leaving every other world and every top-level file untouched.
    ///
    /// <para><b>Refuses a snapshot belonging to a different world.</b> A zip taken from world A
    /// extracted into world B is a silent corruption that looks like a successful restore - the panel
    /// would say "Restored" and the player would find somebody else's base. The world id is in the
    /// snapshot's own path, so the check is free and cannot be forgotten.</para>
    /// </summary>
    public static void RestoreWorld(string snapshotZip, string saveDir, string worldId, string snapshotsDir)
    {
        if (!File.Exists(snapshotZip))
            throw new FileNotFoundException($"Snapshot not found: {snapshotZip}");

        var owner = Path.GetFileName(Path.GetDirectoryName(snapshotZip) ?? "");
        if (!string.Equals(owner, worldId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"That snapshot belongs to a different world ({owner}). Restoring it here would replace " +
                $"{worldId} with somebody else's world.");

        var worldDir = Path.Combine(saveDir, worldId);

        // Same guarantee as the whole-folder restore, smaller blast radius.
        if (Directory.Exists(worldDir) && Directory.EnumerateFileSystemEntries(worldDir).Any())
            BackupWorld(saveDir, worldId, snapshotsDir, "before-restore", auto: true);

        Directory.CreateDirectory(worldDir);
        foreach (var f in Directory.GetFiles(worldDir)) File.Delete(f);
        foreach (var d in Directory.GetDirectories(worldDir)) Directory.Delete(d, recursive: true);

        ZipFile.ExtractToDirectory(snapshotZip, worldDir, overwriteFiles: true);
    }

    /// <summary>One world: the folder name as it sits on disk, when it was last written, how big it
    /// is, and how many files are in it.</summary>
    /// <summary>Whether this is a world you host or one you joined.</summary>
    public enum WorldRole
    {
        /// <summary>The world itself is here - you host it.</summary>
        Hosted,
        /// <summary>Only your local data is here; the world lives on someone else's machine.</summary>
        Joined,
    }

    /// <param name="Name">The folder id on disk - a GUID.</param>
    /// <param name="GameName">What the GAME calls this world, read out of its own save, or null when
    /// there is nothing to read. A world somebody else hosts has no LevelMeta.sav at all. See
    /// <see cref="PalworldWorldName"/>.</param>
    /// <param name="NameBudgetBytes">How many BYTES a rename may occupy, or 0 when the name is not
    /// writable. Not characters - see <see cref="PalworldWorldName"/>.</param>
    public sealed record SaveWorld(string Name, DateTime LastWriteUtc, long Bytes, int FileCount,
        WorldRole Role, int PlayerCount, string? GameName = null, int NameBudgetBytes = 0,
        bool HasOwnSave = false)
    {
        /// <summary>
        /// Who started this world, and how many characters have played in it.
        ///
        /// <para><b>The axis is who started it, not single- versus multi-player</b> - a correction that
        /// came from the game's own World Select screen. Both of the worlds there were hosted, and both
        /// were marked <c>Multiplayer: ON</c>, while this label was calling one of them <i>"solo so
        /// far"</i>. Two words for one object, and ours was the wrong one.</para>
        ///
        /// <para><b>We cannot read the multiplayer setting and must not imply we can.</b> It lives in
        /// <c>WorldOption.sav</c>, whose property region - unlike <c>LevelMeta.sav</c>'s - is
        /// compressed: 4,446 bytes yield exactly one readable string, <c>Difficulty</c>. So this says
        /// only what the filesystem shows.</para>
        ///
        /// <para>What it shows is clear enough. A world you STARTED keeps <c>Level.sav</c> and a
        /// <c>Players</c> folder with one file per character who has played in it. A world you JOINED
        /// keeps only <c>LocalData.sav</c> - your character - because the world itself is on the host's
        /// machine. That is why one of these is 32 MB and the other is 360 KB.</para>
        /// </summary>
        public string RoleLabel => Role switch
        {
            WorldRole.Joined => "Someone else's world - your character only",
            _ when PlayerCount > 1 => $"You started this - {PlayerCount} characters have played here",
            _ => "You started this - 1 character has played here",
        };

        /// <summary>
        /// The thing a player will otherwise think is a bug: a joined world is <b>not in Palworld's
        /// World Select list at all</b>, because there is no world there to load. It appears when you
        /// join the host's session. Empty for worlds the list does show.
        /// </summary>
        public string RoleCaveat => Role == WorldRole.Joined
            ? "Palworld does not list this one - you reach it by joining the host."
            : "";
    }

    /// <summary>
    /// The worlds in a save folder - one per subdirectory that actually contains files.
    ///
    /// <para><see cref="ListSaveFiles"/> reads a single directory level, which is right for a game
    /// that keeps several formats of one save side by side and useless for a game that keeps a folder
    /// per world. Palworld is the latter: 74 .sav files, 72 of them a level down, and the only
    /// top-level one is GlobalPalStorage.sav - shared storage, not anybody's save. Listing that single
    /// file would have implied it WAS the save, which is worse than listing nothing.</para>
    ///
    /// <para>Size and date come from walking the folder, which also picks up the game's own
    /// <c>backup</c> subfolder. Deliberate: the number answers "what does this world cost on disk",
    /// and quietly excluding part of it would make the figure disagree with Explorer.</para>
    ///
    /// <para>A subdirectory holding no files at all is not a world - it is a leftover.</para>
    /// </summary>
    public static IReadOnlyList<SaveWorld> ListWorlds(string saveDir)
    {
        if (!Directory.Exists(saveDir)) return Array.Empty<SaveWorld>();

        var worlds = new List<SaveWorld>();
        foreach (var dir in Directory.GetDirectories(saveDir))
        {
            long bytes = 0;
            var count = 0;
            var last = DateTime.MinValue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var info = new FileInfo(f);
                    bytes += info.Length;
                    count++;
                    if (info.LastWriteTimeUtc > last) last = info.LastWriteTimeUtc;
                }
            }
            catch
            {
                // An unreadable world is skipped rather than failing the list. The panel shows what it
                // can see, and a snapshot covers everything either way.
                continue;
            }

            if (count == 0) continue;

            // Level.sav is the world itself. Its absence means this folder holds only the local data
            // kept for a world somebody else hosts - a distinction worth drawing, since restoring a
            // joined world would not restore anything anyone else can see.
            var hosted = File.Exists(Path.Combine(dir, "Level.sav"));
            var players = 0;
            try
            {
                var pdir = Path.Combine(dir, "Players");
                if (Directory.Exists(pdir)) players = Directory.GetFiles(pdir, "*.sav").Length;
            }
            catch { /* an unreadable Players folder just means we do not know the count */ }

            // What the game itself calls this world. Cheap - LevelMeta.sav is ~2 KB - and it turns a
            // panel full of GUIDs into a panel of names with nothing typed. Null is ordinary.
            var site = PalworldWorldName.Read(dir);

            worlds.Add(new SaveWorld(Path.GetFileName(dir), last, bytes, count,
                hosted ? WorldRole.Hosted : WorldRole.Joined, players,
                site?.Name, site?.BudgetBytes ?? 0, PalworldWorldName.HasOwnSave(dir)));
        }

        // Most recently played first: with GUID folder names the date is the only thing telling two
        // worlds apart until they can be labelled.
        return worlds.OrderByDescending(w => w.LastWriteUtc).ToList();
    }

    /// <summary>
    /// Clone a save to another type — copy <paramref name="sourceFileName"/> to the same base name with
    /// <paramref name="targetExt"/> (e.g. ER0000.sl2 → ER0000.co2). The source is never touched; an
    /// existing target is never overwritten unless <paramref name="overwrite"/> is set (the caller
    /// snapshots first). Returns the new file name.
    /// </summary>
    public static string CloneToType(string saveDir, string sourceFileName, string targetExt, bool overwrite = false)
    {
        var src = System.IO.Path.Combine(saveDir, sourceFileName);
        if (!File.Exists(src)) throw new FileNotFoundException($"Save not found: {sourceFileName}");
        if (!targetExt.StartsWith('.')) targetExt = "." + targetExt;

        var destName = System.IO.Path.GetFileNameWithoutExtension(sourceFileName) + targetExt;
        var dest = System.IO.Path.Combine(saveDir, destName);
        if (File.Exists(dest) && !overwrite)
            throw new IOException($"A {targetExt} save already exists ({destName}). Snapshot it first if you want to replace it.");
        File.Copy(src, dest, overwrite);
        return destName;
    }

    [GeneratedRegex(@"^(\d{8}-\d{6})(?:__(.*))?$")]
    private static partial Regex NameRe();

    // The app's own auto snapshots carry this reserved label prefix so retention can tell them from
    // the user's deliberate backups. Reserved: user labels can never start with it (see Backup).
    private const string AutoPrefix = "auto-";

    private static bool IsAutoLabel(string label) => label.StartsWith(AutoPrefix, StringComparison.OrdinalIgnoreCase);

    public static SaveSnapshot Backup(string saveDir, string snapshotsDir, string? label = null, bool auto = false)
    {
        if (!Directory.Exists(saveDir))
            throw new DirectoryNotFoundException($"Save folder not found: {saveDir}");

        Directory.CreateDirectory(snapshotsDir);
        var takenUtc = DateTime.UtcNow;
        var stamp = takenUtc.ToString(TimeFormat, CultureInfo.InvariantCulture);

        var safe = SanitizeLabel(label);
        // The auto- prefix is reserved for the app: strip it from a user's label so a user backup can
        // never be misclassified as auto (and pruned). Only auto:true mints an auto snapshot.
        while (safe.StartsWith(AutoPrefix, StringComparison.OrdinalIgnoreCase)) safe = safe[AutoPrefix.Length..];
        if (auto) safe = AutoPrefix + (safe.Length > 0 ? safe : "snapshot");

        var fileName = safe.Length > 0 ? $"{stamp}__{safe}.zip" : $"{stamp}.zip";
        var path = System.IO.Path.Combine(snapshotsDir, fileName);

        // A clashing name (same second) would throw; make it unique. n++ each try or this loops forever.
        var n = 1;
        while (File.Exists(path))
            path = System.IO.Path.Combine(snapshotsDir, (safe.Length > 0 ? $"{stamp}__{safe}-{n++}" : $"{stamp}-{n++}") + ".zip");

        ZipFile.CreateFromDirectory(saveDir, path);
        return new SaveSnapshot(path, System.IO.Path.GetFileName(path), safe, takenUtc, new FileInfo(path).Length, IsAutoLabel(safe));
    }

    public static void Restore(string snapshotZip, string saveDir, string snapshotsDir)
    {
        if (!File.Exists(snapshotZip))
            throw new FileNotFoundException($"Snapshot not found: {snapshotZip}");

        // Safety: snapshot the current save state before we overwrite it (auto-tagged).
        if (Directory.Exists(saveDir) && Directory.EnumerateFileSystemEntries(saveDir).Any())
            Backup(saveDir, snapshotsDir, "before-restore", auto: true);

        Directory.CreateDirectory(saveDir);
        foreach (var f in Directory.GetFiles(saveDir)) File.Delete(f);
        foreach (var d in Directory.GetDirectories(saveDir)) Directory.Delete(d, recursive: true);

        ZipFile.ExtractToDirectory(snapshotZip, saveDir, overwriteFiles: true);
    }

    public static IReadOnlyList<SaveSnapshot> ListSnapshots(string snapshotsDir)
    {
        if (!Directory.Exists(snapshotsDir)) return Array.Empty<SaveSnapshot>();
        var outList = new List<SaveSnapshot>();
        foreach (var path in Directory.GetFiles(snapshotsDir, "*.zip"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var m = NameRe().Match(name);
            DateTime taken;
            string label;
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out taken))
                label = m.Groups[2].Success ? m.Groups[2].Value : "";
            else
            {
                taken = File.GetLastWriteTimeUtc(path);
                label = name;
            }
            outList.Add(new SaveSnapshot(path, System.IO.Path.GetFileName(path), label, taken, new FileInfo(path).Length, IsAutoLabel(label)));
        }
        return outList.OrderByDescending(s => s.TakenUtc).ThenByDescending(s => s.FileName, StringComparer.Ordinal).ToList();
    }

    public static void Delete(string snapshotZip)
    {
        if (File.Exists(snapshotZip)) File.Delete(snapshotZip);
    }

    /// <summary>Keep every user snapshot plus the newest <paramref name="keepLastAuto"/> auto snapshots;
    /// delete older autos only. A user (non-auto) snapshot is never deleted.</summary>
    public static void Prune(string snapshotsDir, int keepLastAuto)
    {
        if (keepLastAuto < 0) keepLastAuto = 0;
        var autos = ListSnapshots(snapshotsDir).Where(s => s.IsAuto).ToList(); // ListSnapshots is newest-first
        foreach (var old in autos.Skip(keepLastAuto)) Delete(old.Path);
    }

    /// <summary>The declared save types that actually appear inside a snapshot zip.</summary>
    public static IReadOnlyList<SaveType> TypesInSnapshot(string snapshotZip, IReadOnlyList<SaveType> saveTypes)
    {
        if (!File.Exists(snapshotZip)) return Array.Empty<SaveType>();
        using var zip = ZipFile.OpenRead(snapshotZip);
        var exts = new HashSet<string>(zip.Entries.Select(e => System.IO.Path.GetExtension(e.FullName)), StringComparer.OrdinalIgnoreCase);
        return saveTypes.Where(t => exts.Contains(t.Extension)).ToList();
    }

    /// <summary>Restore only files of <paramref name="extension"/> from a snapshot, leaving other save
    /// types in place. Snapshots current state first (auto) so it stays reversible.</summary>
    public static void RestoreType(string snapshotZip, string saveDir, string snapshotsDir, string extension)
    {
        if (!File.Exists(snapshotZip)) throw new FileNotFoundException($"Snapshot not found: {snapshotZip}");

        if (Directory.Exists(saveDir) && Directory.EnumerateFileSystemEntries(saveDir).Any())
            Backup(saveDir, snapshotsDir, "before-restore", auto: true);

        Directory.CreateDirectory(saveDir);
        using var zip = ZipFile.OpenRead(snapshotZip);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            if (!string.Equals(System.IO.Path.GetExtension(entry.FullName), extension, StringComparison.OrdinalIgnoreCase)) continue;
            var dest = System.IO.Path.Combine(saveDir, entry.FullName);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var cleaned = Regex.Replace(label.Trim(), @"[^A-Za-z0-9 _-]", "");
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }
}
